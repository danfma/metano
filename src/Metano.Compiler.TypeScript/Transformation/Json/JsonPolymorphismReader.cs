using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Reads <c>[JsonPolymorphic]</c> and <c>[JsonDerivedType]</c> from a type's
/// <b>own</b> attributes (both are <c>Inherited = false</c>), matched by
/// their fully qualified metadata names (spec 009 FR-001). A constructed
/// generic base reads the attributes of its definition (FR-015).
/// </summary>
internal static class JsonPolymorphismReader
{
    private const string SerializationNamespace = "System.Text.Json.Serialization";

    private const string DerivedTypeAttribute = "JsonDerivedTypeAttribute";

    private const string PolymorphicAttribute = "JsonPolymorphicAttribute";

    /// <summary>
    /// The polymorphism of <paramref name="type"/>, or <c>null</c> when it
    /// registers no derived type: a type is polymorphic when it has at least
    /// one <c>[JsonDerivedType]</c>, with or without <c>[JsonPolymorphic]</c>.
    /// </summary>
    public static JsonPolymorphism? TryRead(INamedTypeSymbol type)
    {
        var attributes = type.OriginalDefinition.GetAttributes();

        var derived = attributes
            .Where(attribute => IsAttribute(attribute, DerivedTypeAttribute))
            .Select(ToDerivedType)
            .OfType<JsonDerivedType>()
            .ToList();

        if (derived.Count == 0)
            return null;

        var options = attributes
            .Where(attribute => IsAttribute(attribute, PolymorphicAttribute))
            .SelectMany(attribute => attribute.NamedArguments)
            .ToDictionary(argument => argument.Key, argument => argument.Value.Value);

        return new JsonPolymorphism(
            type,
            options.GetValueOrDefault("TypeDiscriminatorPropertyName") as string
                ?? JsonPolymorphism.DefaultDiscriminator,
            (JsonUnknownDerivedTypeHandling)(
                options.GetValueOrDefault("UnknownDerivedTypeHandling") as int? ?? 0
            ),
            options.GetValueOrDefault("IgnoreUnrecognizedTypeDiscriminators") as bool? ?? false,
            derived
        );
    }

    /// <summary>Whether <paramref name="type"/> registers any derived type.</summary>
    public static bool IsPolymorphic(INamedTypeSymbol type) =>
        type
            .OriginalDefinition.GetAttributes()
            .Any(attribute => IsAttribute(attribute, DerivedTypeAttribute));

    private static bool IsAttribute(AttributeData attribute, string metadataName) =>
        attribute.AttributeClass is { } attributeClass
        && attributeClass.MetadataName == metadataName
        && attributeClass.ContainingNamespace.ToDisplayString() == SerializationNamespace;

    /// <summary>
    /// The <c>(Type)</c>, <c>(Type, string)</c> and <c>(Type, int)</c>
    /// constructors; <c>null</c> when the type argument does not resolve.
    /// </summary>
    private static JsonDerivedType? ToDerivedType(AttributeData attribute)
    {
        if (attribute.ConstructorArguments is not [{ Value: ITypeSymbol type }, .. var rest])
            return null;

        var tag = rest switch
        {
            [{ Value: string text }] => new JsonTypeTag.Text(text),
            [{ Value: int number }] => new JsonTypeTag.Number(number),
            _ => (JsonTypeTag?)null,
        };

        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();

        return new JsonDerivedType(type, tag, location);
    }
}
