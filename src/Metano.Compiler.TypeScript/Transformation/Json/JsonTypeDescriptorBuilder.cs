using Metano.Compiler.TypeScript.AST;
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
    /// System.Guid maps to the UUID branded type from metano-runtime.
    /// Other branded types come from user-defined <c>[Branded]</c>
    /// structs and use their TS type name directly.
    /// </summary>
    private TsObjectLiteral BuildBrandedDescriptor(ITypeSymbol type)
    {
        var named = (INamedTypeSymbol)type;

        var tsTypeName =
            named.ToDisplayString() == "System.Guid" ? "UUID" : _context.ResolveTsName(named);

        return new TsObjectLiteral([
            KindProperty(JsonPropertyKind.Branded),
            new TsObjectProperty(
                "create",
                new TsPropertyAccess(new TsIdentifier(tsTypeName), "create")
            ),
        ]);
    }

    private TsObjectLiteral BuildEnumValuesDescriptor(ITypeSymbol type, JsonPropertyKind kind)
    {
        var tsTypeName = _context.ResolveTsName((INamedTypeSymbol)type);

        return new TsObjectLiteral([
            KindProperty(kind),
            new TsObjectProperty("values", new TsIdentifier(tsTypeName)),
        ]);
    }

    private TsObjectLiteral BuildRefDescriptor(ITypeSymbol type)
    {
        var getterName = _names.GetterOf(type);

        return new TsObjectLiteral([
            KindProperty(JsonPropertyKind.Ref),
            new TsObjectProperty(
                "spec",
                new TsArrowFunction(
                    [],
                    [
                        new TsReturnStatement(
                            new TsPropertyAccess(new TsIdentifier("this"), getterName)
                        ),
                    ]
                )
            ),
        ]);
    }

    /// <summary>
    /// Builds a kind-only descriptor for leaf wire kinds with no
    /// payload (primitive / decimal). The runtime <c>SerializerContext</c>
    /// reads the <c>kind</c> field and routes by it without further
    /// fields.
    /// </summary>
    private static TsObjectLiteral BuildBareKindDescriptor(JsonPropertyKind kind) =>
        new([KindProperty(kind)]);
}
