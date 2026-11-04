namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The read behavior a context's <c>[JsonSourceGenerationOptions]</c> sets,
/// mirrored by the runtime <c>JsonReadOptions</c> (spec 009 FR-002, FR-048).
/// Every member is <c>false</c> under System.Text.Json's defaults.
/// </summary>
/// <param name="PropertyNameCaseInsensitive">JSON keys match property names ignoring case.</param>
/// <param name="ReadsNumbersFromStrings">
/// <c>NumberHandling</c> has <c>AllowReadingFromString</c>: numeric values may
/// arrive as JSON strings.
/// </param>
/// <param name="AllowOutOfOrderMetadataProperties">
/// The type discriminator may follow other properties.
/// </param>
internal sealed record JsonContextReadOptions(
    bool PropertyNameCaseInsensitive,
    bool ReadsNumbersFromStrings,
    bool AllowOutOfOrderMetadataProperties
)
{
    public static readonly JsonContextReadOptions Default = new(false, false, false);
}
