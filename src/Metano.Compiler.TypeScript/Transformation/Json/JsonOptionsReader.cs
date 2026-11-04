using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Reads a context's <c>[JsonSourceGenerationOptions]</c>. The
/// <c>JsonSerializerDefaults.Web</c> constructor argument sets the camelCase
/// naming policy, and a named <c>PropertyNamingPolicy</c> overrides it,
/// <c>Unspecified</c> included (spec 009 FR-048 item 1, measured on .NET 10).
/// Only the attribute is read: options passed to the context's constructor at
/// runtime are invisible here.
/// </summary>
internal static class JsonOptionsReader
{
    private const string OptionsAttribute = "JsonSourceGenerationOptionsAttribute";

    private const string NamingPolicyOption = "PropertyNamingPolicy";

    /// <summary>
    /// The JSON naming policy of <paramref name="contextType"/>, or <c>null</c>
    /// when names stay as declared. A <c>PropertyNamingPolicy</c> value outside
    /// <c>JsonKnownNamingPolicy</c> also means no policy.
    /// </summary>
    public static Func<string, string>? NamingPolicyOf(INamedTypeSymbol contextType)
    {
        if (OptionsAttributeOf(contextType) is not { } attribute)
            return null;

        var options = attribute.NamedArguments.ToDictionary(
            argument => argument.Key,
            argument => argument.Value.Value
        );

        if (!options.TryGetValue(NamingPolicyOption, out var namedPolicy))
            return DefaultNamingPolicyOf(attribute);

        return JsonNamingPolicy.FromKnownPolicy((JsonKnownNamingPolicy)(namedPolicy as int? ?? 0));
    }

    private static AttributeData? OptionsAttributeOf(INamedTypeSymbol contextType) =>
        contextType
            .GetAttributes()
            .FirstOrDefault(attribute =>
                JsonSerializationAttributes.Is(attribute, OptionsAttribute)
            );

    /// <summary>
    /// The naming policy the <c>JsonSerializerDefaults</c> constructor argument
    /// sets: camelCase for <c>Web</c>, none for <c>General</c> or no argument.
    /// </summary>
    private static Func<string, string>? DefaultNamingPolicyOf(AttributeData attribute) =>
        attribute.ConstructorArguments is [{ Value: int defaults }]
        && (JsonSerializerDefaults)defaults == JsonSerializerDefaults.Web
            ? JsonNamingPolicy.CamelCase
            : null;
}
