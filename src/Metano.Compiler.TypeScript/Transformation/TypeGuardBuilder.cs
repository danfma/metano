using System.Globalization;
using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Generates the runtime <c>isFoo(value): value is Foo</c> type guards emitted for types
/// marked with <c>[GenerateGuard]</c>.
///
/// Different shapes get different bodies:
/// <list type="bullet">
///   <item>enums → <c>value === "USD" || …</c> (string enum) or <c>typeof value === "number" &amp;&amp; (value === 0 || …)</c></item>
///   <item>interfaces → null/object check + per-field runtime checks</item>
///   <item>records / classes / structs → <c>instanceof</c> fast path + per-field checks</item>
/// </list>
///
/// Field checks recurse into other transpilable guards via the context's
/// <see cref="TypeScriptTransformContext.TranspilableTypes"/>.
/// </summary>
public sealed class TypeGuardBuilder(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    private TsType MapType(ITypeSymbol symbol) =>
        IrToTsTypeMapper.Map(
            IrTypeRefMapper.Map(symbol, _context.OriginResolver, TargetLanguage.TypeScript),
            _context.BclOverrides
        );

    /// <summary>
    /// Returns the pair of functions emitted for a <c>[GenerateGuard]</c>
    /// type: <c>isT(value): value is T</c> (narrowing predicate) and
    /// <c>assertT(value, message?): asserts value is T</c> (throwing
    /// variant that wraps <c>isT</c>). Consumers typically use the first
    /// in conditionals and the second at trust boundaries (parsing JSON,
    /// accepting <c>unknown</c> from a network handler) where an
    /// exception is the natural failure mode. Returns an empty list when
    /// the type doesn't need a guard (exceptions, ExportedAsModule,
    /// NoContainer, types with extension members, types imported from
    /// external modules).
    /// </summary>
    public IReadOnlyList<TsFunction> Generate(INamedTypeSymbol type)
    {
        if (TryBuildPredicate(type) is not { } predicate)
            return [];

        var tsName = _context.ResolveTsName(type);
        var ownName = _context.ResolveOwnTsName(type);

        return
        [
            predicate.Guard,
            GenerateAssert(tsName, ownName, predicate.Guard.Name, predicate.PredicateType),
        ];
    }

    /// <summary>
    /// The <c>isT</c> predicate alone, without the <c>assertT</c> companion:
    /// the guard a polymorphic JSON hierarchy synthesizes for a
    /// <c>[PlainObject]</c> type that does not declare <c>[GenerateGuard]</c>
    /// (FR-019 rule 1). <c>null</c> when the type gets no guard (see
    /// <see cref="Generate"/>).
    /// </summary>
    public TsFunction? GeneratePredicate(INamedTypeSymbol type) => TryBuildPredicate(type)?.Guard;

    /// <summary>
    /// The <c>isT</c> function of <paramref name="type"/> and the type it
    /// narrows to, or <c>null</c> when the type gets no guard.
    /// </summary>
    private (TsFunction Guard, TsType PredicateType)? TryBuildPredicate(INamedTypeSymbol type)
    {
        if (TypeTransformer.IsExceptionType(type))
            return null;
        if (
            SymbolHelper.HasExportedAsModule(type)
            || SymbolHelper.HasNoContainer(type)
            || TypeTransformer.HasExtensionMembers(type)
        )
            return null;
        if (SymbolHelper.HasImport(type))
            return null;

        // A private nested type is declared under its module-local identifier (#412).
        // The predicate type and the unexported guard and assert identifiers spell it,
        // so two private `Circle`s, or a private `Circle` next to a module's own
        // `isCircle`, never clash; the error message reads the type's own name.
        var tsName = _context.ResolveTsName(type);
        var guardName = $"is{tsName}";
        var valueParam = new TsParameter("value", new TsNamedType("unknown"));

        TsFunction? guard = null;
        TsType predicateType = new TsNamedType(tsName);

        var unionVariants = TryFindDiscriminatedVariants(type);
        if (unionVariants is { Count: > 0 })
        {
            predicateType = BuildVariantUnionType(unionVariants);
            guard = GenerateUnionGuard(type, guardName, predicateType, valueParam, unionVariants);
        }
        else if (type.TypeKind == TypeKind.Enum)
            guard = GenerateEnumGuard(type, guardName, tsName, valueParam);
        else if (type.TypeKind == TypeKind.Interface)
            guard = GenerateShapeGuard(type, guardName, tsName, valueParam, useInstanceof: false);
        else if (type.IsRecord || type.TypeKind is TypeKind.Struct or TypeKind.Class)
        {
            // [PlainObject] records/classes emit as bare TS interfaces —
            // no class is available at runtime, so the `instanceof` fast
            // path would reference an identifier that only exists in the
            // type position and fail with TS2693. Shape validation still
            // narrows correctly for those. Regular records keep the fast
            // path.
            var useInstanceof = !SymbolHelper.HasPlainObject(type);
            guard = GenerateShapeGuard(type, guardName, tsName, valueParam, useInstanceof);
        }

        return guard is null ? null : (guard, predicateType);
    }

    private IReadOnlyList<INamedTypeSymbol>? TryFindDiscriminatedVariants(INamedTypeSymbol baseType)
    {
        if (baseType.TypeKind is not (TypeKind.Class or TypeKind.Interface))
            return null;
        var baseField = SymbolHelper.GetDiscriminatorFieldName(baseType);
        if (baseField is null)
            return null;
        if (baseType.TypeKind == TypeKind.Class && !baseType.IsAbstract)
            return null;

        var variants = new List<INamedTypeSymbol>();
        foreach (var candidate in EnumerateAssemblyTypes(_context.CurrentAssembly))
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, baseType))
                continue;
            if (!IsDerivedFrom(candidate, baseType))
                continue;
            if (!SymbolHelper.HasGenerateGuard(candidate))
                continue;
            if (
                !SymbolHelper.IsTranspilable(
                    candidate,
                    _context.AssemblyWideTranspile,
                    _context.CurrentAssembly
                )
            )
                continue;
            var variantField = SymbolHelper.GetDiscriminatorFieldName(candidate);
            if (
                variantField is not null
                && !string.Equals(variantField, baseField, StringComparison.Ordinal)
            )
                continue;
            variants.Add(candidate);
        }

        if (variants.Count == 0)
            return null;
        variants.Sort(
            (a, b) =>
                string.Compare(
                    _context.ResolveTsName(a),
                    _context.ResolveTsName(b),
                    StringComparison.Ordinal
                )
        );
        return variants;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateAssemblyTypes(IAssemblySymbol? assembly)
    {
        if (assembly is null)
            yield break;
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(assembly.GlobalNamespace);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                    stack.Push(member);
            }
            else if (current is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in type.GetTypeMembers())
                    stack.Push(nested);
            }
        }
    }

    private static bool IsDerivedFrom(INamedTypeSymbol candidate, INamedTypeSymbol baseType)
    {
        if (baseType.TypeKind == TypeKind.Interface)
        {
            foreach (var i in candidate.AllInterfaces)
                if (
                    SymbolEqualityComparer.Default.Equals(
                        i.OriginalDefinition,
                        baseType.OriginalDefinition
                    )
                )
                    return true;
            return false;
        }
        var current = candidate.BaseType;
        while (current is not null)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    baseType.OriginalDefinition
                )
            )
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private TsType BuildVariantUnionType(IReadOnlyList<INamedTypeSymbol> variants) =>
        new TsUnionType(
            variants.Select(v => (TsType)new TsNamedType(_context.ResolveTsName(v))).ToList()
        );

    private TsFunction GenerateUnionGuard(
        INamedTypeSymbol baseType,
        string guardName,
        TsType predicateType,
        TsParameter valueParam,
        IReadOnlyList<INamedTypeSymbol> variants
    )
    {
        var body = new List<TsStatement>
        {
            new TsIfStatement(
                new TsBinaryExpression(
                    new TsBinaryExpression(new TsIdentifier("value"), "==", new TsLiteral("null")),
                    "||",
                    new TsBinaryExpression(
                        new TsUnaryExpression("typeof ", new TsIdentifier("value")),
                        "!==",
                        new TsStringLiteral("object")
                    )
                ),
                [new TsReturnStatement(new TsLiteral("false"))]
            ),
            new TsVariableDeclaration(
                "v",
                new TsCastExpression(new TsIdentifier("value"), new TsAnyType())
            ),
        };

        var discriminatorTsName = DiscriminatorTsNameOf(baseType)!;

        var discriminatorMatch = variants
            .Select<INamedTypeSymbol, TsExpression>(v => new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("v"), discriminatorTsName),
                "===",
                new TsStringLiteral(_context.ResolveOwnTsName(v))
            ))
            .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

        if (SymbolHelper.HasStrictUnionGuard(baseType))
            AppendStrictUnionBody(body, discriminatorMatch, discriminatorTsName);
        else
            body.Add(new TsReturnStatement(discriminatorMatch));

        return new TsFunction(
            guardName,
            [valueParam],
            new TsTypePredicateType("value", predicateType),
            body
        );
    }

    /// <summary>
    /// Strict path: short-circuit <c>false</c> when the discriminator
    /// doesn't match any known variant, look the variant guard up in
    /// the runtime registry, delegate to it when present, fall back to
    /// <c>true</c> otherwise. The fallback keeps the strict guard at
    /// least as permissive as the legacy discriminator-only narrow —
    /// never tighter when variant modules aren't loaded, so opting in
    /// can't accidentally start rejecting previously-accepted shapes.
    /// </summary>
    private static void AppendStrictUnionBody(
        List<TsStatement> body,
        TsExpression discriminatorMatch,
        string discriminatorTsName
    )
    {
        body.Add(
            new TsIfStatement(
                new TsUnaryExpression("!", discriminatorMatch),
                [new TsReturnStatement(new TsLiteral("false"))]
            )
        );
        body.Add(
            new TsVariableDeclaration(
                "guard",
                new TsCallExpression(
                    new TsIdentifier("getUnionGuard"),
                    [new TsPropertyAccess(new TsIdentifier("v"), discriminatorTsName)]
                )
            )
        );
        body.Add(
            new TsReturnStatement(
                new TsConditionalExpression(
                    new TsBinaryExpression(
                        new TsIdentifier("guard"),
                        "!==",
                        new TsLiteral("undefined")
                    ),
                    new TsCallExpression(new TsIdentifier("guard"), [new TsIdentifier("value")]),
                    new TsLiteral("true")
                )
            )
        );
    }

    /// <summary>
    /// When the variant participates in a <c>[StrictUnionGuard]</c>
    /// hierarchy, returns the side-effect statement that registers the
    /// variant's guard on the shared <c>UnionGuardRegistry</c> at module
    /// load. Emitted as a top-level <c>registerUnionGuard("Circle",
    /// isCircle)</c> call alongside the generated <c>isCircle</c>
    /// function. Returns <c>null</c> for types whose base hierarchy
    /// doesn't opt into strict guards — keeping the default emission
    /// path untouched.
    /// </summary>
    public TsTopLevelStatement? TryBuildVariantRegistration(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return null;
        if (!SymbolHelper.HasGenerateGuard(type))
            return null;

        var strictBase = FindStrictUnionBase(type);
        if (strictBase is null)
            return null;

        var variantTsName = _context.ResolveOwnTsName(type);
        var guardName = $"is{_context.ResolveTsName(type)}";

        // The discriminator value the registry keys on is the variant's
        // own TS name — matches the per-variant guard convention
        // (`v.kind === "Circle"`). Reading the variant's own
        // discriminator field isn't needed: the registry's key space is
        // the discriminator-value vocabulary, and the variant's emitted
        // discriminator literal is its TS name.
        var call = new TsCallExpression(
            new TsIdentifier("registerUnionGuard"),
            [new TsStringLiteral(variantTsName), new TsIdentifier(guardName)]
        );

        return new TsTopLevelStatement(new TsExpressionStatement(call));
    }

    private static INamedTypeSymbol? FindStrictUnionBase(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current is not null && current.SpecialType == SpecialType.None)
        {
            if (
                SymbolHelper.HasStrictUnionGuard(current)
                && SymbolHelper.HasGenerateGuard(current)
                && SymbolHelper.GetDiscriminatorFieldName(current) is not null
            )
                return current;
            current = current.BaseType;
        }
        foreach (var iface in type.AllInterfaces)
        {
            if (
                SymbolHelper.HasStrictUnionGuard(iface)
                && SymbolHelper.HasGenerateGuard(iface)
                && SymbolHelper.GetDiscriminatorFieldName(iface) is not null
            )
                return iface;
        }
        return null;
    }

    private static TsFunction GenerateAssert(
        string tsName,
        string ownName,
        string guardName,
        TsType predicateType
    )
    {
        var valueParam = new TsParameter("value", new TsNamedType("unknown"));
        var messageParam = new TsParameter("message", new TsNamedType("string"), Optional: true);

        // throw new TypeError(message ?? "Value is not a TName");
        var defaultMessage = new TsStringLiteral($"Value is not a {ownName}");
        var throwStmt = new TsThrowStatement(
            new TsNewExpression(
                new TsIdentifier("TypeError"),
                [new TsBinaryExpression(new TsIdentifier("message"), "??", defaultMessage)]
            )
        );

        // if (!isT(value)) { throw ... }
        var body = new List<TsStatement>
        {
            new TsIfStatement(
                new TsUnaryExpression(
                    "!",
                    new TsCallExpression(new TsIdentifier(guardName), [new TsIdentifier("value")])
                ),
                [throwStmt]
            ),
        };

        return new TsFunction(
            $"assert{tsName}",
            [valueParam, messageParam],
            new TsTypePredicateType("value", predicateType, IsAsserts: true),
            body
        );
    }

    private static TsFunction GenerateEnumGuard(
        INamedTypeSymbol type,
        string guardName,
        string tsName,
        TsParameter valueParam
    )
    {
        var isStringEnum = SymbolHelper.HasStringEnum(type);
        var members = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .ToList();

        TsExpression condition;

        if (isStringEnum)
        {
            // value === "BRL" || value === "USD" || ...
            condition = members
                .Select<IFieldSymbol, TsExpression>(m =>
                {
                    var name = SymbolHelper.GetNameOverride(m, TargetLanguage.TypeScript) ?? m.Name;
                    return new TsBinaryExpression(
                        new TsIdentifier("value"),
                        "===",
                        new TsStringLiteral(name)
                    );
                })
                .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));
        }
        else
        {
            // typeof value === "number" && (value === 0 || value === 1 || ...)
            var valueChecks = members
                .Select<IFieldSymbol, TsExpression>(m => new TsBinaryExpression(
                    new TsIdentifier("value"),
                    "===",
                    new TsLiteral(m.ConstantValue!.ToString()!)
                ))
                .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

            condition = new TsBinaryExpression(
                new TsBinaryExpression(
                    new TsUnaryExpression("typeof ", new TsIdentifier("value")),
                    "===",
                    new TsStringLiteral("number")
                ),
                "&&",
                valueChecks
            );
        }

        return new TsFunction(
            guardName,
            [valueParam],
            new TsTypePredicateType("value", new TsNamedType(tsName)),
            [new TsReturnStatement(condition)]
        );
    }

    private TsFunction GenerateShapeGuard(
        INamedTypeSymbol type,
        string guardName,
        string tsName,
        TsParameter valueParam,
        bool useInstanceof
    )
    {
        var body = new List<TsStatement>();

        // instanceof fast path (for classes/records only)
        if (useInstanceof)
        {
            body.Add(
                new TsIfStatement(
                    new TsBinaryExpression(
                        new TsIdentifier("value"),
                        "instanceof",
                        new TsIdentifier(tsName)
                    ),
                    [new TsReturnStatement(new TsLiteral("true"))]
                )
            );
        }

        // Null/object check
        body.Add(
            new TsIfStatement(
                new TsBinaryExpression(
                    new TsBinaryExpression(new TsIdentifier("value"), "==", new TsLiteral("null")),
                    "||",
                    new TsBinaryExpression(
                        new TsUnaryExpression("typeof ", new TsIdentifier("value")),
                        "!==",
                        new TsStringLiteral("object")
                    )
                ),
                [new TsReturnStatement(new TsLiteral("false"))]
            )
        );

        // const v = value as any;
        body.Add(
            new TsVariableDeclaration(
                "v",
                new TsCastExpression(new TsIdentifier("value"), new TsAnyType())
            )
        );

        // [Discriminator("FieldName")] short-circuit: check the named
        // field against the type's TS name (convention: enum member
        // name matches the type's TS name — e.g., Circle class tags
        // `Kind` and the emitted guard expects `v.kind === "Circle"`).
        // Runs before shape validation so a mismatch exits the guard
        // immediately instead of walking every field. The frontend
        // validator (MS0011) guarantees the discriminant is a present
        // non-nullable StringEnum, so the literal comparison is safe.
        // Resolves the TS field name via the same rule the shape loop
        // uses (`[Name(TypeScript, …)]` override ∪ camelCase), so a
        // renamed discriminator surfaces on both the short-circuit
        // access and the skip filter below.
        var discriminatorTsName = DiscriminatorTsNameOf(type);
        if (discriminatorTsName is not null)
        {
            body.Add(
                new TsIfStatement(
                    new TsBinaryExpression(
                        new TsPropertyAccess(new TsIdentifier("v"), discriminatorTsName),
                        "!==",
                        new TsStringLiteral(_context.ResolveOwnTsName(type))
                    ),
                    [new TsReturnStatement(new TsLiteral("false"))]
                )
            );
        }

        // Field checks — skip the discriminator (already narrowed above)
        // to avoid redundant recursion into isKind(v.kind).
        var fields = GetAllFieldsForGuard(type)
            .Where(f => !string.Equals(f.Name, discriminatorTsName, StringComparison.Ordinal))
            .ToList();
        if (fields.Count > 0)
        {
            TsExpression fieldChecks = fields
                .Select(f =>
                    GenerateFieldCheck(new TsPropertyAccess(new TsIdentifier("v"), f.Name), f.Type)
                )
                .Aggregate((a, b) => new TsBinaryExpression(a, "&&", b));

            body.Add(new TsReturnStatement(fieldChecks));
        }
        else
        {
            body.Add(new TsReturnStatement(new TsLiteral("true")));
        }

        return new TsFunction(
            guardName,
            [valueParam],
            new TsTypePredicateType("value", new TsNamedType(tsName)),
            body
        );
    }

    /// <summary>
    /// The members the shape guard of a <c>[PlainObject]</c> record or class
    /// tests, with the kind of each test and whether a missing member fails
    /// it (FR-019 rule 3). Built from the same field list and the same
    /// per-field tests <see cref="GenerateShapeGuard"/> emits; a
    /// <c>[Discriminator]</c> member is compared to the type's own TS name.
    /// </summary>
    internal IReadOnlyList<GuardedMember> GuardedMembersOf(INamedTypeSymbol type)
    {
        var discriminatorTsName = DiscriminatorTsNameOf(type);

        return GetAllFieldsForGuard(type)
            .Select(field =>
                field.Name == discriminatorTsName
                    ? new GuardedMember(
                        field.Name,
                        GuardTestKind.String,
                        IsRequired: true,
                        Literal: _context.ResolveOwnTsName(type)
                    )
                    : ToGuardedMember(field.Name, field.Type)
            )
            .ToList();
    }

    private GuardedMember ToGuardedMember(string name, TsType type)
    {
        // Only the test's kind is read: the access expression is a stand-in
        // for the guard's `v.<name>`.
        var test = FieldTestOf(new TsPropertyAccess(new TsIdentifier("v"), name), type);

        return new GuardedMember(name, test.Kind, IsRequired: !test.AcceptsAbsent);
    }

    /// <summary>
    /// The TS name of the <c>[Discriminator]</c> member of
    /// <paramref name="type"/> (its <c>[Name]</c> override, else camelCase),
    /// or <c>null</c> when it declares none. One rule for the guard's
    /// short-circuit, its field filter and the union guard.
    /// </summary>
    private static string? DiscriminatorTsNameOf(INamedTypeSymbol type)
    {
        if (SymbolHelper.GetDiscriminatorFieldName(type) is not { } fieldName)
            return null;

        var member = type.GetMembers(fieldName).OfType<IPropertySymbol>().FirstOrDefault();
        var nameOverride = member?.GetNameOverride(TargetLanguage.TypeScript);

        return nameOverride ?? TypeScriptNaming.ToCamelCase(fieldName);
    }

    /// <summary>
    /// Gets all fields (own + inherited) for guard validation.
    /// </summary>
    private IReadOnlyList<(string Name, TsType Type)> GetAllFieldsForGuard(INamedTypeSymbol type)
    {
        var fields = new List<(string Name, TsType Type)>();

        // Collect from all levels of hierarchy
        var current = type;
        while (
            current is not null
            && current.SpecialType == SpecialType.None
            && current.ToDisplayString() is not "System.Object" and not "System.ValueType"
        )
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsImplicitlyDeclared)
                    continue;
                if (member.IsStatic)
                    continue;
                // Unified policy: exclude Private, Internal, and NotApplicable.
                // ProtectedAndInternal (C# `private protected`) and
                // ProtectedOrInternal (C# `protected internal`) are treated as
                // TS `protected` — included in the guard because TS has no
                // assembly-level visibility distinction.
                if (!IsGuardVisible(member.DeclaredAccessibility))
                    continue;
                if (SymbolHelper.HasIgnore(member, TargetLanguage.TypeScript))
                    continue;

                var name =
                    SymbolHelper.GetNameOverride(member, TargetLanguage.TypeScript)
                    ?? TypeScriptNaming.ToCamelCase(member.Name);
                var tsType = MapType(member.Type);

                // Avoid duplicates (from overrides)
                if (fields.All(f => f.Name != name))
                    fields.Add((name, tsType));
            }

            foreach (var member in current.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.IsImplicitlyDeclared)
                    continue;
                if (member.IsStatic)
                    continue;
                if (member.AssociatedSymbol is not null)
                    continue;
                if (!IsGuardVisible(member.DeclaredAccessibility))
                    continue;
                if (SymbolHelper.HasIgnore(member, TargetLanguage.TypeScript))
                    continue;

                var name =
                    SymbolHelper.GetNameOverride(member, TargetLanguage.TypeScript)
                    ?? TypeScriptNaming.ToCamelCase(member.Name);
                var tsType = MapType(member.Type);

                if (fields.All(f => f.Name != name))
                    fields.Add((name, tsType));
            }

            current = current.BaseType;
        }

        return fields;
    }

    /// <summary>
    /// Returns true when a member's declared accessibility should be included in a
    /// generated type guard. TS has no assembly-level visibility, so both C# composite
    /// accessibilities (<c>private protected</c> and <c>protected internal</c>) are
    /// treated as plain <c>protected</c> — visible to subclasses, included in the
    /// guard. Only <c>Private</c>, <c>Internal</c>, and <c>NotApplicable</c> are
    /// excluded.
    /// </summary>
    private static bool IsGuardVisible(Accessibility accessibility) =>
        accessibility
            is Accessibility.Public
                or Accessibility.Protected
                or Accessibility.ProtectedOrInternal
                or Accessibility.ProtectedAndInternal;

    /// <summary>
    /// Generates a runtime type check expression for a single field.
    /// </summary>
    private TsExpression GenerateFieldCheck(TsExpression fieldAccess, TsType fieldType) =>
        FieldTestOf(fieldAccess, fieldType).Check;

    /// <summary>
    /// The check a guard emits for one field and the coarse kind it amounts
    /// to (FR-019 rule 3). Both come from this one switch, so the specificity
    /// order the JSON context computes always follows the emitted guard.
    /// </summary>
    private GuardFieldTest FieldTestOf(TsExpression fieldAccess, TsType fieldType) =>
        fieldType switch
        {
            TsNumberType => Required(TypeofCheck(fieldAccess, "number"), GuardTestKind.Number),
            TsStringType => Required(TypeofCheck(fieldAccess, "string"), GuardTestKind.String),
            TsBooleanType => Required(TypeofCheck(fieldAccess, "boolean"), GuardTestKind.Boolean),
            TsBigIntType => Required(TypeofCheck(fieldAccess, "bigint"), GuardTestKind.BigInt),

            TsArrayType => Required(ArrayCheck(fieldAccess), GuardTestKind.Array),

            TsNamedType { Name: "Map" or "Set" } named => Required(
                InstanceofCheck(fieldAccess, named.Name),
                GuardTestKind.Object
            ),

            // Temporal types
            TsNamedType { Name: var n } when n.StartsWith("Temporal.") => Required(
                InstanceofCheck(fieldAccess, n),
                GuardTestKind.Object
            ),

            // Transpilable named type with its own guard → call it. The guard
            // may test an enum value or an object, so its kind is presence only.
            TsNamedType { Name: var n } when HasGuard(n) => Required(
                new TsCallExpression(new TsIdentifier($"is{n}"), [fieldAccess]),
                GuardTestKind.Present
            ),

            // Union with null (nullable) → field == null || innerCheck
            TsUnionType { Types: var types }
                when types.Any(t => t is TsNamedType { Name: "null" }) => NullableFieldTest(
                fieldAccess,
                types
            ),

            // String literal union (from StringEnum that's not transpilable).
            TsUnionType { Types: var types } when types.All(t => t is TsStringLiteralType) =>
                Required(
                    StringLiteralUnionCheck(fieldAccess, types.Cast<TsStringLiteralType>()),
                    GuardTestKind.String
                ),

            TsTupleType { Elements: var elements } => Required(
                TupleCheck(fieldAccess, elements.Count),
                GuardTestKind.Array
            ),

            TsAnyType or TsVoidType or TsPromiseType => Unchecked(),

            // A named type without a guard of this compilation: a type of
            // this project that declares no guard, or a cross-package /
            // cross-assembly type (the TranspilableTypes dict only carries
            // current-assembly entries). Emit a presence check so the field
            // can't silently be missing from the input. Uses the
            // loose-equality convention from ADR-0014 so `undefined` and
            // `null` collapse to the same "absent" case.
            TsNamedType => Required(
                new TsBinaryExpression(fieldAccess, "!=", new TsLiteral("null")),
                GuardTestKind.Present
            ),

            // Unknown shape the switch doesn't cover — accept anything.
            // Reaches this branch only for TsType variants the builder
            // does not know about (new AST kinds); safer to keep the
            // field permissive than to reject valid shapes.
            _ => Unchecked(),
        };

    /// <summary>
    /// Whether <c>is{tsName}</c> is a guard this compilation emits and the
    /// import collector resolves: a type of this project with
    /// <c>[GenerateGuard]</c>, or a <c>[PlainObject]</c> type whose guard a
    /// polymorphic JSON hierarchy synthesizes (FR-019). Any other type gets a
    /// presence check instead of a call to a function that does not exist.
    /// </summary>
    private bool HasGuard(string tsName) => _context.TryResolveGuardImport($"is{tsName}", out _);

    private static GuardFieldTest Required(TsExpression check, GuardTestKind kind) =>
        new(check, kind, AcceptsAbsent: false);

    private static GuardFieldTest Unchecked() =>
        new(new TsLiteral("true"), GuardTestKind.Unchecked, AcceptsAbsent: true);

    private static TsExpression TypeofCheck(TsExpression expr, string typeName) =>
        new TsBinaryExpression(
            new TsUnaryExpression("typeof ", expr),
            "===",
            new TsStringLiteral(typeName)
        );

    private static TsExpression StringLiteralUnionCheck(
        TsExpression fieldAccess,
        IEnumerable<TsStringLiteralType> literals
    ) =>
        literals
            .Select<TsStringLiteralType, TsExpression>(literal => new TsBinaryExpression(
                fieldAccess,
                "===",
                new TsStringLiteral(literal.Value)
            ))
            .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

    private static TsBinaryExpression TupleCheck(TsExpression fieldAccess, int length) =>
        new(
            ArrayCheck(fieldAccess),
            "&&",
            new TsBinaryExpression(
                new TsPropertyAccess(fieldAccess, "length"),
                "===",
                new TsLiteral(length.ToString(CultureInfo.InvariantCulture))
            )
        );

    private static TsCallExpression ArrayCheck(TsExpression fieldAccess) =>
        new(new TsPropertyAccess(new TsIdentifier("Array"), "isArray"), [fieldAccess]);

    private static TsBinaryExpression InstanceofCheck(TsExpression fieldAccess, string typeName) =>
        new(fieldAccess, "instanceof", new TsIdentifier(typeName));

    /// <summary>
    /// <c>field == null || innerCheck</c>: the member may be absent, and its
    /// kind is the inner test's when there is a single one.
    /// </summary>
    private GuardFieldTest NullableFieldTest(
        TsExpression fieldAccess,
        IReadOnlyList<TsType> unionTypes
    )
    {
        var innerTests = unionTypes
            .Where(t => t is not TsNamedType { Name: "null" })
            .Select(t => FieldTestOf(fieldAccess, t))
            .ToList();

        if (innerTests.Count == 0)
            return Unchecked();

        var innerCheck = innerTests
            .Select(test => test.Check)
            .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

        var kind = innerTests.Count == 1 ? innerTests[0].Kind : GuardTestKind.Present;

        return new GuardFieldTest(
            new TsBinaryExpression(
                new TsBinaryExpression(fieldAccess, "==", new TsLiteral("null")),
                "||",
                innerCheck
            ),
            kind,
            AcceptsAbsent: true
        );
    }
}
