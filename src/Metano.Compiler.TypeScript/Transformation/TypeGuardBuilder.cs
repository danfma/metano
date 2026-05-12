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
        if (TypeTransformer.IsExceptionType(type))
            return [];
        if (
            SymbolHelper.HasExportedAsModule(type)
            || SymbolHelper.HasNoContainer(type)
            || TypeTransformer.HasExtensionMembers(type)
        )
            return [];
        if (SymbolHelper.HasImport(type))
            return [];

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

        if (guard is null)
            return [];

        return [guard, GenerateAssert(tsName, guardName, predicateType)];
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

        var discriminatorField = SymbolHelper.GetDiscriminatorFieldName(baseType)!;
        var discriminatorMember = baseType
            .GetMembers(discriminatorField)
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
        var discriminatorTsName =
            (
                discriminatorMember is not null
                    ? SymbolHelper.GetNameOverride(discriminatorMember, TargetLanguage.TypeScript)
                    : null
            ) ?? TypeScriptNaming.ToCamelCase(discriminatorField);

        var discriminatorMatch = variants
            .Select<INamedTypeSymbol, TsExpression>(v => new TsBinaryExpression(
                new TsPropertyAccess(new TsIdentifier("v"), discriminatorTsName),
                "===",
                new TsStringLiteral(_context.ResolveTsName(v))
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
                new TsUnaryExpression("!", new TsParenthesized(discriminatorMatch)),
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

        var variantTsName = _context.ResolveTsName(type);
        var guardName = $"is{variantTsName}";

        // The discriminator value the registry keys on is the variant's
        // TS name — matches the per-variant guard convention
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
        string guardName,
        TsType? predicateType = null
    )
    {
        var valueParam = new TsParameter("value", new TsNamedType("unknown"));
        var messageParam = new TsParameter("message", new TsNamedType("string"), Optional: true);

        // throw new TypeError(message ?? "Value is not a TName");
        var defaultMessage = new TsStringLiteral($"Value is not a {tsName}");
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
            new TsTypePredicateType(
                "value",
                predicateType ?? new TsNamedType(tsName),
                IsAsserts: true
            ),
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
                new TsParenthesized(valueChecks)
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
        var discriminatorFieldName = SymbolHelper.GetDiscriminatorFieldName(type);
        string? discriminatorTsName = null;
        if (discriminatorFieldName is not null)
        {
            var discriminatorMember = type.GetMembers(discriminatorFieldName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();
            discriminatorTsName =
                (
                    discriminatorMember is not null
                        ? SymbolHelper.GetNameOverride(
                            discriminatorMember,
                            TargetLanguage.TypeScript
                        )
                        : null
                ) ?? TypeScriptNaming.ToCamelCase(discriminatorFieldName);

            body.Add(
                new TsIfStatement(
                    new TsBinaryExpression(
                        new TsPropertyAccess(new TsIdentifier("v"), discriminatorTsName),
                        "!==",
                        new TsStringLiteral(tsName)
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
    private TsExpression GenerateFieldCheck(TsExpression fieldAccess, TsType fieldType)
    {
        return fieldType switch
        {
            TsNumberType => TypeofCheck(fieldAccess, "number"),
            TsStringType => TypeofCheck(fieldAccess, "string"),
            TsBooleanType => TypeofCheck(fieldAccess, "boolean"),
            TsBigIntType => TypeofCheck(fieldAccess, "bigint"),

            TsArrayType => new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                [fieldAccess]
            ),

            TsNamedType { Name: "Map" } => new TsBinaryExpression(
                fieldAccess,
                "instanceof",
                new TsIdentifier("Map")
            ),

            TsNamedType { Name: "Set" } => new TsBinaryExpression(
                fieldAccess,
                "instanceof",
                new TsIdentifier("Set")
            ),

            // Temporal types
            TsNamedType { Name: var n } when n.StartsWith("Temporal.") => new TsBinaryExpression(
                fieldAccess,
                "instanceof",
                new TsIdentifier(n)
            ),

            // Transpilable named type → call guard recursively
            TsNamedType { Name: var n } when _context.TranspilableTypes.ContainsKey(n) =>
                new TsCallExpression(new TsIdentifier($"is{n}"), [fieldAccess]),

            // Union with null (nullable) → field == null || innerCheck
            TsUnionType { Types: var types }
                when types.Any(t => t is TsNamedType { Name: "null" }) => NullableFieldCheck(
                fieldAccess,
                types
            ),

            // String literal union (from StringEnum that's not transpilable).
            // Wrapped so the OR chain stays grouped when it lands in an
            // outer AND conjunction — same precedence concern as
            // NullableFieldCheck above.
            TsUnionType { Types: var types } when types.All(t => t is TsStringLiteralType) =>
                new TsParenthesized(
                    types
                        .Cast<TsStringLiteralType>()
                        .Select<TsStringLiteralType, TsExpression>(t => new TsBinaryExpression(
                            fieldAccess,
                            "===",
                            new TsStringLiteral(t.Value)
                        ))
                        .Aggregate((a, b) => new TsBinaryExpression(a, "||", b))
                ),

            TsTupleType { Elements: var elements } => new TsBinaryExpression(
                new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                    [fieldAccess]
                ),
                "&&",
                new TsBinaryExpression(
                    new TsPropertyAccess(fieldAccess, "length"),
                    "===",
                    new TsLiteral(elements.Count.ToString())
                )
            ),

            TsAnyType or TsVoidType or TsPromiseType => new TsLiteral("true"),

            // Cross-package / cross-assembly named type that didn't
            // match any specific case above — the TranspilableTypes
            // dict only carries current-assembly entries, so referenced
            // enums / records land here. Full recursion into their
            // guards requires cross-package guard resolution (tracked
            // as a follow-up); for now emit a presence check so the
            // field can't silently be missing from the input. Uses the
            // loose-equality convention from ADR-0014 so `undefined`
            // and `null` collapse to the same "absent" case.
            TsNamedType => new TsBinaryExpression(fieldAccess, "!=", new TsLiteral("null")),

            // Unknown shape the switch doesn't cover — accept anything.
            // Reaches this branch only for TsType variants the builder
            // does not know about (new AST kinds); safer to keep the
            // field permissive than to reject valid shapes.
            _ => new TsLiteral("true"),
        };
    }

    private static TsExpression TypeofCheck(TsExpression expr, string typeName) =>
        new TsBinaryExpression(
            new TsUnaryExpression("typeof ", expr),
            "===",
            new TsStringLiteral(typeName)
        );

    private TsExpression NullableFieldCheck(
        TsExpression fieldAccess,
        IReadOnlyList<TsType> unionTypes
    )
    {
        var nonNullTypes = unionTypes.Where(t => t is not TsNamedType { Name: "null" }).ToList();
        if (nonNullTypes.Count == 0)
            return new TsLiteral("true");

        var innerCheck =
            nonNullTypes.Count == 1
                ? GenerateFieldCheck(fieldAccess, nonNullTypes[0])
                : nonNullTypes
                    .Select(t => GenerateFieldCheck(fieldAccess, t))
                    .Aggregate((a, b) => new TsBinaryExpression(a, "||", b));

        // Parenthesize the `null || inner` disjunction — JS `&&` binds
        // tighter than `||`, so when this expression lands in an
        // AND-chain with other field checks
        // (`typeof v.a === "number" && nullable-check && …`) the
        // grouping must stay `a && (b || c) && d` instead of
        // accidentally associating as `a && b || c && d`.
        return new TsParenthesized(
            new TsBinaryExpression(
                new TsBinaryExpression(fieldAccess, "==", new TsLiteral("null")),
                "||",
                innerCheck
            )
        );
    }
}
