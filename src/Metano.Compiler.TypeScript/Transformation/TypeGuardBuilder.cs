using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.Extraction;
using Metano.TypeScript.AST;
using Metano.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Transformation;

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
    /// types with extension members, types imported from external
    /// modules).
    /// </summary>
    public IReadOnlyList<TsFunction> Generate(INamedTypeSymbol type)
    {
        if (TypeTransformer.IsExceptionType(type))
            return [];
        if (SymbolHelper.HasExportedAsModule(type) || TypeTransformer.HasExtensionMembers(type))
            return [];
        if (SymbolHelper.HasImport(type))
            return [];

        var tsName = _context.ResolveTsName(type);
        var guardName = $"is{tsName}";
        var valueParam = new TsParameter("value", new TsNamedType("unknown"));

        TsFunction? guard = null;
        if (type.TypeKind == TypeKind.Enum)
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

        return [guard, GenerateAssert(tsName, guardName)];
    }

    /// <summary>
    /// Builds the throwing <c>assertT</c> companion for the predicate
    /// <c>isT</c> generated on the same type. The body is a thin wrapper
    /// that negates <c>isT</c> and throws <see cref="TypeError"/> with
    /// either the caller-supplied <paramref name="message"/> parameter
    /// or a default <c>"Value is not a TName"</c> fallback. Kept inline
    /// (no <c>metano-runtime</c> helper) so the guard stays zero-dep
    /// and tree-shakable, matching ADR-0009's accepted trade-off.
    /// </summary>
    private static TsFunction GenerateAssert(string tsName, string guardName)
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
            new TsTypePredicateType("value", new TsNamedType(tsName), IsAsserts: true),
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
        var discriminatorFieldName = SymbolHelper.GetDiscriminatorFieldName(type);
        var discriminatorCamel = discriminatorFieldName is not null
            ? TypeScriptNaming.ToCamelCase(discriminatorFieldName)
            : null;
        if (discriminatorFieldName is not null && discriminatorCamel is not null)
        {
            body.Add(
                new TsIfStatement(
                    new TsBinaryExpression(
                        new TsPropertyAccess(new TsIdentifier("v"), discriminatorCamel),
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
            .Where(f => !string.Equals(f.Name, discriminatorCamel, StringComparison.Ordinal))
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

            // String literal union (from StringEnum that's not transpilable)
            TsUnionType { Types: var types } when types.All(t => t is TsStringLiteralType) => types
                .Cast<TsStringLiteralType>()
                .Select<TsStringLiteralType, TsExpression>(t => new TsBinaryExpression(
                    fieldAccess,
                    "===",
                    new TsStringLiteral(t.Value)
                ))
                .Aggregate((a, b) => new TsBinaryExpression(a, "||", b)),

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

        return new TsBinaryExpression(
            new TsBinaryExpression(fieldAccess, "==", new TsLiteral("null")),
            "||",
            innerCheck
        );
    }
}
