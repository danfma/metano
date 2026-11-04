using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Recognizes the System.Text.Json serialization attributes by their fully
/// qualified metadata name, so a same-named attribute declared in another
/// namespace is ignored, and reads the arguments the generated context
/// mirrors. <c>[JsonIgnore]</c> is the exception: it is matched by its short
/// name (<see cref="IgnoreConditionOf"/>), so an ignore attribute of another
/// JSON library also hides the member.
/// </summary>
internal static class JsonSerializationAttributes
{
    public const string Constructor = "JsonConstructorAttribute";

    public const string Converter = "JsonConverterAttribute";

    public const string DerivedType = "JsonDerivedTypeAttribute";

    public const string ExtensionData = "JsonExtensionDataAttribute";

    public const string Include = "JsonIncludeAttribute";

    public const string NumberHandling = "JsonNumberHandlingAttribute";

    public const string ObjectCreationHandling = "JsonObjectCreationHandlingAttribute";

    public const string Polymorphic = "JsonPolymorphicAttribute";

    public const string PropertyName = "JsonPropertyNameAttribute";

    public const string PropertyOrder = "JsonPropertyOrderAttribute";

    public const string Required = "JsonRequiredAttribute";

    public const string SourceGenerationOptions = "JsonSourceGenerationOptionsAttribute";

    public const string UnmappedMemberHandling = "JsonUnmappedMemberHandlingAttribute";

    private const string IgnoreShortName = "JsonIgnore";

    private const string IgnoreAttribute = "JsonIgnoreAttribute";

    private const string SerializationNamespace = "System.Text.Json.Serialization";

    /// <summary>
    /// Whether <paramref name="attribute"/> is
    /// <c>System.Text.Json.Serialization.<paramref name="metadataName"/></c>.
    /// </summary>
    public static bool Is(AttributeData attribute, string metadataName) =>
        attribute.AttributeClass is { } attributeClass
        && attributeClass.MetadataName == metadataName
        && attributeClass.ContainingNamespace.ToDisplayString() == SerializationNamespace;

    /// <summary>
    /// The first <c>System.Text.Json.Serialization.<paramref name="metadataName"/></c>
    /// on <paramref name="symbol"/>, or <c>null</c>.
    /// </summary>
    public static AttributeData? Find(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute => Is(attribute, metadataName));

    /// <summary>Whether <paramref name="symbol"/> carries <paramref name="metadataName"/>.</summary>
    public static bool Has(ISymbol symbol, string metadataName) =>
        Find(symbol, metadataName) is not null;

    /// <summary>
    /// The <c>Condition</c> of the member's <c>[JsonIgnore]</c>
    /// (<c>Always</c> when the attribute names none), or <c>null</c> without
    /// the attribute, matched by its short name.
    /// </summary>
    public static JsonIgnoreCondition? IgnoreConditionOf(ISymbol member)
    {
        var attribute = member
            .GetAttributes()
            .FirstOrDefault(candidate =>
                candidate.AttributeClass?.Name is IgnoreShortName or IgnoreAttribute
            );

        if (attribute is null)
            return null;

        return attribute
            .NamedArguments.Where(argument => argument.Key == "Condition")
            .Select(argument => (JsonIgnoreCondition)(argument.Value.Value as int? ?? 0))
            .DefaultIfEmpty(JsonIgnoreCondition.Always)
            .First();
    }

    /// <summary>
    /// Whether the member's <c>[JsonIgnore]</c> hides it both ways
    /// (<c>Always</c>, the default condition).
    /// </summary>
    public static bool IsAlwaysIgnored(ISymbol member) =>
        IgnoreConditionOf(member) == JsonIgnoreCondition.Always;

    /// <summary>
    /// The <c>[JsonPropertyOrder]</c> of <paramref name="member"/>, <c>0</c>
    /// without one (System.Text.Json's default).
    /// </summary>
    public static int OrderOf(ISymbol member) =>
        Find(member, PropertyOrder) is { ConstructorArguments: [{ Value: int order }] } ? order : 0;

    /// <summary>
    /// The enum value of the attribute's single constructor argument
    /// (<c>[JsonNumberHandling(…)]</c>, <c>[JsonObjectCreationHandling(…)]</c>,
    /// <c>[JsonUnmappedMemberHandling(…)]</c>), <c>0</c> when absent.
    /// </summary>
    public static int EnumArgumentOf(AttributeData attribute) =>
        attribute.ConstructorArguments is [{ Value: int value }] ? value : 0;
}
