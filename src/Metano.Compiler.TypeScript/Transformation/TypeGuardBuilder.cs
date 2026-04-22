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
    /// Returns null when the type doesn't need a guard (exceptions, ExportedAsModule,
    /// types with extension members, types imported from external modules).
    /// </summary>
    public TsFunction? Generate(INamedTypeSymbol type)
    {
        if (TypeTransformer.IsExceptionType(type))
            return null;
        if (SymbolHelper.HasExportedAsModule(type) || TypeTransformer.HasExtensionMembers(type))
            return null;
        if (SymbolHelper.HasImport(type))
            return null;

        var tsName = _context.ResolveTsName(type);
        var guardName = $"is{tsName}";
        var valueParam = new TsParameter("value", new TsNamedType("unknown"));

        if (type.TypeKind == TypeKind.Enum)
            return GenerateEnumGuard(type, guardName, tsName, valueParam);

        if (type.TypeKind == TypeKind.Interface)
            return GenerateShapeGuard(type, guardName, tsName, valueParam, useInstanceof: false);

        if (type.IsRecord || type.TypeKind is TypeKind.Struct or TypeKind.Class)
            return GenerateShapeGuard(type, guardName, tsName, valueParam, useInstanceof: true);

        return null;
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

        // Field checks
        var fields = GetAllFieldsForGuard(type);
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

            // Unknown type — accept anything
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
