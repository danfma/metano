using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Reads a context's <c>[JsonSourceGenerationOptions]</c>, matched by its fully
/// qualified metadata name. The <c>JsonSerializerDefaults.Web</c> constructor
/// argument sets the camelCase naming policy, and a named
/// <c>PropertyNamingPolicy</c> overrides it, <c>Unspecified</c> included
/// (spec 009 FR-048 item 1, measured on .NET 10).
/// </summary>
internal static class JsonOptionsReader
{
    private const string SerializationNamespace = "System.Text.Json.Serialization";

    private const string OptionsAttribute = "JsonSourceGenerationOptionsAttribute";

    private const string NamingPolicyOption = "PropertyNamingPolicy";

    private const string WebDefaults = "Web";

    /// <summary>
    /// The JSON naming policy of <paramref name="contextType"/>, or <c>null</c>
    /// when names stay as declared.
    /// </summary>
    public static Func<string, string>? NamingPolicyOf(INamedTypeSymbol contextType)
    {
        if (OptionsAttributeOf(contextType) is not { } attribute)
            return null;

        var namedPolicy = attribute.NamedArguments.FirstOrDefault(argument =>
            argument.Key == NamingPolicyOption
        );

        return namedPolicy.Key is null
            ? DefaultNamingPolicyOf(attribute)
            : JsonNamingPolicy.FromKnownPolicy(EnumMemberNameOf(namedPolicy.Value));
    }

    private static AttributeData? OptionsAttributeOf(INamedTypeSymbol contextType) =>
        contextType
            .GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass is { } attributeClass
                && attributeClass.MetadataName == OptionsAttribute
                && attributeClass.ContainingNamespace.ToDisplayString() == SerializationNamespace
            );

    /// <summary>
    /// The naming policy the <c>JsonSerializerDefaults</c> constructor argument
    /// sets: camelCase for <c>Web</c>, none for <c>General</c> or no argument.
    /// </summary>
    private static Func<string, string>? DefaultNamingPolicyOf(AttributeData attribute) =>
        attribute.ConstructorArguments is [var defaults]
        && EnumMemberNameOf(defaults) == WebDefaults
            ? JsonNamingPolicy.CamelCase
            : null;

    private static string? EnumMemberNameOf(TypedConstant constant) =>
        constant.Type is INamedTypeSymbol enumType
            ? enumType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(member =>
                    member.HasConstantValue && Equals(member.ConstantValue, constant.Value)
                )
                ?.Name
            : null;
}
