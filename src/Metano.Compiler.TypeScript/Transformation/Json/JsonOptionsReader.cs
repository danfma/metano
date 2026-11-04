using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Reads a context's <c>[JsonSourceGenerationOptions]</c>. The
/// <c>JsonSerializerDefaults.Web</c> constructor argument sets the camelCase
/// naming policy, case-insensitive property names and numbers read from
/// strings; a named option overrides its Web default, <c>Unspecified</c> /
/// <c>false</c> / <c>Strict</c> included (spec 009 FR-048, FR-002, measured on
/// .NET 10). Only the attribute is read: options passed to the context's
/// constructor at runtime are invisible here.
/// </summary>
internal static class JsonOptionsReader
{
    private const string NamingPolicyOption = "PropertyNamingPolicy";

    private const string CaseInsensitiveOption = "PropertyNameCaseInsensitive";

    private const string NumberHandlingOption = "NumberHandling";

    private const string OutOfOrderMetadataOption = "AllowOutOfOrderMetadataProperties";

    /// <summary>
    /// The JSON naming policy of <paramref name="contextType"/>, or <c>null</c>
    /// when names stay as declared. A <c>PropertyNamingPolicy</c> value outside
    /// <c>JsonKnownNamingPolicy</c> also means no policy.
    /// </summary>
    public static Func<string, string>? NamingPolicyOf(INamedTypeSymbol contextType)
    {
        if (OptionsAttributeOf(contextType) is not { } attribute)
            return null;

        if (!NamedOptionsOf(attribute).TryGetValue(NamingPolicyOption, out var namedPolicy))
            return HasWebDefaults(attribute) ? JsonNamingPolicy.CamelCase : null;

        return JsonNamingPolicy.FromKnownPolicy((JsonKnownNamingPolicy)(namedPolicy as int? ?? 0));
    }

    /// <summary>
    /// The read options of <paramref name="contextType"/>: the Web defaults
    /// (<c>PropertyNameCaseInsensitive = true</c>,
    /// <c>NumberHandling = AllowReadingFromString</c>) unless a named option
    /// overrides them, and <c>AllowOutOfOrderMetadataProperties</c>, which is
    /// <c>false</c> under both defaults. Only the <c>AllowReadingFromString</c>
    /// flag of <c>NumberHandling</c> is mirrored.
    /// </summary>
    public static JsonContextReadOptions ReadOptionsOf(INamedTypeSymbol contextType)
    {
        if (OptionsAttributeOf(contextType) is not { } attribute)
            return JsonContextReadOptions.Default;

        var options = NamedOptionsOf(attribute);
        var webDefaults = HasWebDefaults(attribute);

        return new JsonContextReadOptions(
            PropertyNameCaseInsensitive: BoolOptionOf(options, CaseInsensitiveOption, webDefaults),
            ReadsNumbersFromStrings: NumberHandlingOf(options, webDefaults)
                .HasFlag(JsonNumberHandling.AllowReadingFromString),
            AllowOutOfOrderMetadataProperties: BoolOptionOf(
                options,
                OutOfOrderMetadataOption,
                defaultValue: false
            )
        );
    }

    private static AttributeData? OptionsAttributeOf(INamedTypeSymbol contextType) =>
        contextType
            .GetAttributes()
            .FirstOrDefault(attribute =>
                JsonSerializationAttributes.Is(
                    attribute,
                    JsonSerializationAttributes.SourceGenerationOptions
                )
            );

    /// <summary>
    /// The scalar named options of <paramref name="attribute"/>. An array
    /// option (<c>Converters</c>) has no single value and maps to
    /// <c>null</c>: reading <c>Value</c> of an array constant throws.
    /// </summary>
    private static Dictionary<string, object?> NamedOptionsOf(AttributeData attribute) =>
        attribute.NamedArguments.ToDictionary(
            argument => argument.Key,
            argument => argument.Value.Kind == TypedConstantKind.Array ? null : argument.Value.Value
        );

    /// <summary>
    /// Whether the <c>JsonSerializerDefaults</c> constructor argument is
    /// <c>Web</c>; <c>General</c> and no argument are not.
    /// </summary>
    private static bool HasWebDefaults(AttributeData attribute) =>
        attribute.ConstructorArguments is [{ Value: int defaults }]
        && (JsonSerializerDefaults)defaults == JsonSerializerDefaults.Web;

    /// <summary>The named boolean option <paramref name="name"/>, else <paramref name="defaultValue"/>.</summary>
    private static bool BoolOptionOf(
        Dictionary<string, object?> options,
        string name,
        bool defaultValue
    ) => options.TryGetValue(name, out var named) ? named is true : defaultValue;

    /// <summary>The named <c>NumberHandling</c>, else the defaults' one.</summary>
    private static JsonNumberHandling NumberHandlingOf(
        Dictionary<string, object?> options,
        bool webDefaults
    )
    {
        if (options.TryGetValue(NumberHandlingOption, out var named))
            return (JsonNumberHandling)(named as int? ?? 0);

        return webDefaults ? JsonNumberHandling.AllowReadingFromString : JsonNumberHandling.Strict;
    }
}
