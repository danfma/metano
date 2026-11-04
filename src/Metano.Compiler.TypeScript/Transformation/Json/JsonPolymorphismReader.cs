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

    private const string DiscriminatorOption = "TypeDiscriminatorPropertyName";

    private const string UnknownDerivedTypeOption = "UnknownDerivedTypeHandling";

    private const string IgnoreUnrecognizedOption = "IgnoreUnrecognizedTypeDiscriminators";

    /// <summary>
    /// The polymorphism of <paramref name="type"/>, or <c>null</c> when it
    /// carries neither attribute. A type is polymorphic when it has at least
    /// one <c>[JsonDerivedType]</c>; a <c>[JsonPolymorphic]</c> without any is
    /// read with no registration, which the validator rejects as
    /// System.Text.Json does.
    /// </summary>
    public static JsonPolymorphism? TryRead(INamedTypeSymbol type)
    {
        var registrations = DerivedTypeAttributesOf(type)
            .Select(ToRegistration)
            .OfType<JsonDerivedTypeRegistration>()
            .ToList();

        var polymorphicAttribute = type
            .OriginalDefinition.GetAttributes()
            .FirstOrDefault(attribute => IsAttribute(attribute, PolymorphicAttribute));

        if (registrations.Count == 0 && polymorphicAttribute is null)
            return null;

        var options =
            polymorphicAttribute?.NamedArguments.ToDictionary(
                argument => argument.Key,
                argument => argument.Value.Value
            ) ?? [];

        var discriminator =
            options.GetValueOrDefault(DiscriminatorOption) as string
            ?? JsonPolymorphism.DefaultDiscriminator;

        var unknownDerivedType = (JsonUnknownDerivedTypeHandling)(
            options.GetValueOrDefault(UnknownDerivedTypeOption) as int?
            ?? (int)JsonUnknownDerivedTypeHandling.FailSerialization
        );

        var ignoreUnrecognizedTags =
            options.GetValueOrDefault(IgnoreUnrecognizedOption) as bool? ?? false;

        var location = LocationOf(polymorphicAttribute) ?? registrations.FirstOrDefault()?.Location;

        return new JsonPolymorphism(
            type,
            discriminator,
            unknownDerivedType,
            ignoreUnrecognizedTags,
            registrations,
            location
        );
    }

    /// <summary>Whether <paramref name="type"/> registers any derived type.</summary>
    public static bool IsPolymorphic(INamedTypeSymbol type) => DerivedTypeAttributesOf(type).Any();

    private static IEnumerable<AttributeData> DerivedTypeAttributesOf(INamedTypeSymbol type) =>
        type
            .OriginalDefinition.GetAttributes()
            .Where(attribute => IsAttribute(attribute, DerivedTypeAttribute));

    private static bool IsAttribute(AttributeData attribute, string metadataName) =>
        attribute.AttributeClass is { } attributeClass
        && attributeClass.MetadataName == metadataName
        && attributeClass.ContainingNamespace.ToDisplayString() == SerializationNamespace;

    /// <summary>
    /// The <c>(Type)</c>, <c>(Type, string)</c> and <c>(Type, int)</c>
    /// constructors; <c>null</c> when the type argument does not resolve.
    /// </summary>
    private static JsonDerivedTypeRegistration? ToRegistration(AttributeData attribute)
    {
        if (attribute.ConstructorArguments is not [{ Value: ITypeSymbol type }, .. var rest])
            return null;

        var tag = rest switch
        {
            [{ Value: string text }] => new JsonTypeTag.Text(text),
            [{ Value: int number }] => new JsonTypeTag.Number(number),
            _ => (JsonTypeTag?)null,
        };

        return new JsonDerivedTypeRegistration(type, tag, LocationOf(attribute));
    }

    private static Location? LocationOf(AttributeData? attribute) =>
        attribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation();
}
