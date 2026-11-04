using System.Text.Json;
using System.Text.Json.Serialization;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>MS0037</c> warnings of a context's <c>[JsonSourceGenerationOptions]</c>
/// (spec 009 FR-046): one per named option the generated context does not
/// mirror and that is set to a value other than System.Text.Json's default,
/// which is the same under <c>JsonSerializerDefaults.General</c> and
/// <c>Web</c> for each of them (research R8). Every named option of the
/// attribute on .NET 10 is classified here or mirrored by
/// <see cref="JsonOptionsReader"/>:
/// <list type="bullet">
///   <item>mirrored: <c>PropertyNamingPolicy</c>, <c>PropertyNameCaseInsensitive</c>,
///     <c>AllowOutOfOrderMetadataProperties</c> and the
///     <c>AllowReadingFromString</c> flag of <c>NumberHandling</c>;</item>
///   <item>never reported, because they do not change the JSON value:
///     <c>WriteIndented</c>, <c>IndentCharacter</c>, <c>IndentSize</c>,
///     <c>NewLine</c> (formatting belongs to the caller's
///     <c>JSON.stringify</c>), <c>DefaultBufferSize</c> and
///     <c>GenerationMode</c>;</item>
///   <item>reported: every other option (<see cref="UnmirroredDefaults"/>),
///     <c>Converters</c> when it lists a converter, and each other
///     <c>NumberHandling</c> flag.</item>
/// </list>
/// </summary>
internal static class JsonUnmirroredContextOptions
{
    private const string OptionsAttribute = "JsonSourceGenerationOptionsAttribute";

    private const string NumberHandlingOption = "NumberHandling";

    private const string ConvertersOption = "Converters";

    private const string MaxDepthOption = "MaxDepth";

    /// <summary>
    /// The depth limit the runtime enforces (DECIDED-20 b); <c>0</c> means
    /// the same default in System.Text.Json.
    /// </summary>
    private const int MirroredMaxDepth = 64;

    /// <summary>
    /// The unmirrored options with a single value each, and System.Text.Json's
    /// default for it: setting the default changes nothing and is not
    /// reported.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, object> UnmirroredDefaults = new Dictionary<
        string,
        object
    >
    {
        ["DefaultIgnoreCondition"] = (int)JsonIgnoreCondition.Never,
        ["IgnoreReadOnlyProperties"] = false,
        ["IgnoreReadOnlyFields"] = false,
        ["IncludeFields"] = false,
        ["UseStringEnumConverter"] = false,
        ["ReferenceHandler"] = (int)JsonKnownReferenceHandler.Unspecified,
        ["DictionaryKeyPolicy"] = (int)JsonKnownNamingPolicy.Unspecified,
        ["ReadCommentHandling"] = (int)JsonCommentHandling.Disallow,
        ["AllowTrailingCommas"] = false,
        ["AllowDuplicateProperties"] = true,
        ["UnmappedMemberHandling"] = (int)JsonUnmappedMemberHandling.Skip,
        ["UnknownTypeHandling"] = (int)JsonUnknownTypeHandling.JsonElement,
        ["PreferredObjectCreationHandling"] = (int)JsonObjectCreationHandling.Replace,
        ["RespectNullableAnnotations"] = false,
        ["RespectRequiredConstructorParameters"] = false,
    };

    /// <summary>The warnings of <paramref name="contextType"/>'s options, in argument order.</summary>
    public static IEnumerable<MetanoDiagnostic> Of(INamedTypeSymbol contextType)
    {
        var attribute = JsonSerializationAttributes.Find(contextType, OptionsAttribute);

        if (attribute is null)
            return [];

        return attribute.NamedArguments.SelectMany(argument =>
            UnmirroredSettingsOf(argument.Key, argument.Value)
                .Select(setting =>
                    JsonUnmirroredOption.Warning(
                        Message(setting),
                        JsonUnmirroredOption.LocationOf(attribute, argument.Key)
                    )
                )
        );
    }

    /// <summary>
    /// The names under which one named argument is reported: the option
    /// itself, or one <c>NumberHandling.&lt;flag&gt;</c> per unmirrored flag.
    /// </summary>
    private static IEnumerable<string> UnmirroredSettingsOf(string option, TypedConstant value) =>
        option switch
        {
            NumberHandlingOption => UnmirroredNumberHandlingFlagsOf(value),
            ConvertersOption => value.Values.IsDefaultOrEmpty ? [] : [option],
            MaxDepthOption => value.Value is 0 or MirroredMaxDepth ? [] : [option],
            _ when UnmirroredDefaults.TryGetValue(option, out var defaultValue) => Equals(
                value.Value,
                defaultValue
            )
                ? []
                : [option],
            _ => [],
        };

    /// <summary>
    /// The <c>NumberHandling</c> flags other than the mirrored
    /// <c>AllowReadingFromString</c> (FR-048): <c>WriteAsString</c> writes
    /// numbers as JSON strings and <c>AllowNamedFloatingPointLiterals</c>
    /// reads and writes <c>"NaN"</c> / <c>"Infinity"</c> (measured in S2b).
    /// </summary>
    private static IEnumerable<string> UnmirroredNumberHandlingFlagsOf(TypedConstant value)
    {
        var handling = (JsonNumberHandling)(value.Value as int? ?? 0);

        return Enum.GetValues<JsonNumberHandling>()
            .Where(flag => flag != JsonNumberHandling.Strict)
            .Where(flag => flag != JsonNumberHandling.AllowReadingFromString)
            .Where(flag => handling.HasFlag(flag))
            .Select(flag => $"{NumberHandlingOption}.{flag}");
    }

    private static string Message(string setting) =>
        $"'{setting}' changes the JSON wire format, but the generated TypeScript context does "
        + "not mirror it.";
}
