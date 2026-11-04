using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Builds the runtime <c>TypeDescriptor</c> object literal of a C# type, the
/// same for a property and for a value root (FR-030). Nested descriptors
/// (nullable inner, element, key, value) recurse through
/// <see cref="JsonTypeClassifier"/>, and every <c>ref</c> names its target
/// getter through <see cref="JsonTypeInfoNames"/>.
/// </summary>
internal sealed class JsonTypeDescriptorBuilder(
    TypeScriptTransformContext context,
    JsonTypeInfoNames names
)
{
    private readonly TypeScriptTransformContext _context = context;
    private readonly JsonTypeInfoNames _names = names;

    /// <summary>The descriptor of <paramref name="type"/>, classified first.</summary>
    public TsObjectLiteral Build(ITypeSymbol type) =>
        Build(type, JsonTypeClassifier.Classify(type));

    /// <summary>
    /// Dispatches to the per-kind descriptor builder. Compiler-enforced
    /// exhaustiveness: a new <see cref="JsonPropertyKind"/> value will
    /// fail to build if no arm is added here.
    /// </summary>
    public TsObjectLiteral Build(ITypeSymbol type, JsonPropertyKind kind) =>
        kind switch
        {
            JsonPropertyKind.Nullable => BuildNullableDescriptor(type),
            JsonPropertyKind.Array => BuildElementDescriptor(
                kind,
                JsonTypeClassifier.ElementTypeOf(type)
            ),
            JsonPropertyKind.HashSet => BuildElementDescriptor(
                kind,
                JsonTypeClassifier.SetElementTypeOf(type)
            ),
            JsonPropertyKind.Map => BuildMapDescriptor(type),
            JsonPropertyKind.Temporal => BuildTemporalDescriptor(type),
            JsonPropertyKind.Branded => BuildBrandedDescriptor(type),
            JsonPropertyKind.Enum or JsonPropertyKind.NumericEnum => BuildEnumValuesDescriptor(
                type,
                kind
            ),
            JsonPropertyKind.Ref => BuildRefDescriptor(type),
            JsonPropertyKind.Primitive or JsonPropertyKind.Decimal => BuildBareKindDescriptor(kind),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
        };

    /// <summary>
    /// The lazy link to the spec of <paramref name="type"/>,
    /// <c>() =&gt; this.&lt;getter&gt;</c>: getters reference each other, so
    /// every link to another spec is a thunk.
    /// </summary>
    public TsArrowFunction BuildSpecLink(ITypeSymbol type) =>
        new(
            [],
            [
                new TsReturnStatement(
                    new TsPropertyAccess(new TsIdentifier("this"), _names.GetterOf(type))
                ),
            ]
        );

    /// <summary>
    /// Wire-format string for the runtime <c>TypeDescriptor.kind</c>
    /// field. Kept as a single-source-of-truth mapping so the on-disk
    /// shape can never drift from the enum.
    /// </summary>
    private static string GetWireKind(JsonPropertyKind kind) =>
        kind switch
        {
            JsonPropertyKind.Primitive => "primitive",
            JsonPropertyKind.Nullable => "nullable",
            JsonPropertyKind.Branded => "branded",
            JsonPropertyKind.Decimal => "decimal",
            JsonPropertyKind.Temporal => "temporal",
            JsonPropertyKind.Map => "map",
            JsonPropertyKind.HashSet => "hashSet",
            JsonPropertyKind.Array => "array",
            JsonPropertyKind.Enum => "enum",
            JsonPropertyKind.NumericEnum => "numericEnum",
            JsonPropertyKind.Ref => "ref",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
        };

    private static TsObjectProperty KindProperty(JsonPropertyKind kind) =>
        new("kind", new TsStringLiteral(GetWireKind(kind)));

    private TsObjectLiteral BuildNullableDescriptor(ITypeSymbol type) =>
        new([
            KindProperty(JsonPropertyKind.Nullable),
            new TsObjectProperty("inner", Build(JsonTypeClassifier.UnwrapNullable(type))),
        ]);

    /// <summary>An <c>array</c> or <c>hashSet</c> descriptor over its element.</summary>
    private TsObjectLiteral BuildElementDescriptor(
        JsonPropertyKind kind,
        ITypeSymbol elementType
    ) => new([KindProperty(kind), new TsObjectProperty("element", Build(elementType))]);

    private TsObjectLiteral BuildMapDescriptor(ITypeSymbol type) =>
        new([
            KindProperty(JsonPropertyKind.Map),
            new TsObjectProperty("key", Build(JsonTypeClassifier.KeyTypeOf(type))),
            new TsObjectProperty("value", Build(JsonTypeClassifier.ValueTypeOf(type))),
        ]);

    private TsObjectLiteral BuildTemporalDescriptor(ITypeSymbol type)
    {
        var typeName = JsonTsTypes.Map(_context, type) is TsNamedType namedTs
            ? namedTs.Name
            : "Temporal.PlainDateTime";

        return new TsObjectLiteral([
            KindProperty(JsonPropertyKind.Temporal),
            new TsObjectProperty("parse", new TsPropertyAccess(new TsIdentifier(typeName), "from")),
        ]);
    }

    /// <summary>
    /// System.Guid maps to the UUID branded type from metano-runtime. Other
    /// branded types come from user-defined <c>[Branded]</c> structs and use
    /// their TS type reference (qualified when nested). <c>create</c> is the
    /// companion's non-validating factory unless the type declares a valid
    /// <c>[JsonFactory]</c>, whose companion member the descriptor names
    /// instead (FR-021).
    /// </summary>
    private TsObjectLiteral BuildBrandedDescriptor(ITypeSymbol type)
    {
        var (typeName, member) = BrandedFactoryTarget((INamedTypeSymbol)type);

        return new TsObjectLiteral([
            KindProperty(JsonPropertyKind.Branded),
            new TsObjectProperty(
                "create",
                new TsPropertyAccess(QualifiedReference(typeName), member)
            ),
        ]);
    }

    /// <summary>
    /// The companion a branded descriptor's <c>create</c> points at: the type
    /// reference and the member name. <c>System.Guid</c> is <c>UUID.create</c>;
    /// any other type uses its valid <c>[JsonFactory]</c>, else <c>create</c>.
    /// </summary>
    private (string TypeName, string Member) BrandedFactoryTarget(INamedTypeSymbol type)
    {
        if (type.ToDisplayString() == "System.Guid")
        {
            return ("UUID", IrToTsBrandedBridge.CreateMember);
        }

        var factory = JsonBrandedFactoryResolver.Resolve(type);

        return (TypeReferenceName(type), factory?.TsName ?? IrToTsBrandedBridge.CreateMember);
    }

    /// <summary>
    /// The TS type reference of <paramref name="type"/>: the mapped type name,
    /// qualified for a nested type (<c>Outer.Inner</c>) as property types
    /// reference it.
    /// </summary>
    private string TypeReferenceName(INamedTypeSymbol type) =>
        JsonTsTypes.Map(_context, type) is TsNamedType namedTs
            ? namedTs.Name
            : _context.ResolveTsName(type);

    /// <summary>
    /// A possibly qualified type reference as an expression: <c>Episode.Title</c>
    /// is <c>Episode</c> accessed by <c>Title</c>, so the import collector sees
    /// the root binding as a value use instead of an unresolvable dotted name.
    /// </summary>
    private static TsExpression QualifiedReference(string name)
    {
        var segments = name.Split('.');

        return segments
            .Skip(1)
            .Aggregate<string, TsExpression>(
                new TsIdentifier(segments[0]),
                (target, segment) => new TsPropertyAccess(target, segment)
            );
    }

    private TsObjectLiteral BuildEnumValuesDescriptor(ITypeSymbol type, JsonPropertyKind kind)
    {
        var tsTypeName = _context.ResolveTsName((INamedTypeSymbol)type);

        return new TsObjectLiteral([
            KindProperty(kind),
            new TsObjectProperty("values", new TsIdentifier(tsTypeName)),
        ]);
    }

    private TsObjectLiteral BuildRefDescriptor(ITypeSymbol type) =>
        new([
            KindProperty(JsonPropertyKind.Ref),
            new TsObjectProperty("spec", BuildSpecLink(type)),
        ]);

    /// <summary>
    /// Builds a kind-only descriptor for leaf wire kinds with no
    /// payload (primitive / decimal). The runtime <c>SerializerContext</c>
    /// reads the <c>kind</c> field and routes by it without further
    /// fields.
    /// </summary>
    private static TsObjectLiteral BuildBareKindDescriptor(JsonPropertyKind kind) =>
        new([KindProperty(kind)]);
}
