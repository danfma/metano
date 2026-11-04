using System.Globalization;
using System.Text;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Compile-time naming policies that mirror System.Text.Json.JsonKnownNamingPolicy.
/// Used to pre-compute JSON wire names during transpilation.
/// </summary>
internal static class JsonNamingPolicy
{
    /// <summary>
    /// Resolves a naming policy function from a JsonKnownNamingPolicy enum value name.
    /// Returns null for the default policy (preserve PascalCase).
    /// </summary>
    public static Func<string, string>? FromKnownPolicy(string? policyName) =>
        policyName switch
        {
            "CamelCase" => CamelCase,
            "SnakeCaseLower" => SnakeCaseLower,
            "SnakeCaseUpper" => SnakeCaseUpper,
            "KebabCaseLower" => KebabCaseLower,
            "KebabCaseUpper" => KebabCaseUpper,
            _ => null,
        };

    /// <summary>
    /// Mirrors System.Text.Json's <c>JsonCamelCaseNamingPolicy</c>: lowercases
    /// the leading uppercase run except its last letter when a non-uppercase
    /// character follows it (measured on .NET 10: <c>URL</c> → <c>url</c>,
    /// <c>IOStream</c> → <c>ioStream</c>, <c>ABC_D</c> → <c>abC_D</c>,
    /// <c>Item_Name</c> → <c>item_Name</c>, <c>_Leading</c> unchanged).
    /// </summary>
    public static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var i = 0;
        while (i < name.Length && char.IsUpper(name[i]))
            i++;

        if (i == 0)
            return name;
        if (i == 1)
            return char.ToLowerInvariant(name[0]) + name[1..];
        if (i == name.Length)
            return name.ToLowerInvariant();

        // Acronym at start: "HTMLParser" → "htmlParser"
        return name[..(i - 1)].ToLowerInvariant() + name[(i - 1)..];
    }

    public static string SnakeCaseLower(string name) => Separate(name, '_', lowercase: true);

    public static string SnakeCaseUpper(string name) => Separate(name, '_', lowercase: false);

    public static string KebabCaseLower(string name) => Separate(name, '-', lowercase: true);

    public static string KebabCaseUpper(string name) => Separate(name, '-', lowercase: false);

    /// <summary>
    /// A port of System.Text.Json's <c>JsonSeparatorNamingPolicy</c>, so the
    /// snake and kebab policies write the names .NET writes (measured on
    /// .NET 10: <c>Item_Name</c> → <c>item_name</c>, <c>Value2</c> →
    /// <c>value2</c>, <c>A1B</c> → <c>a1_b</c>, <c>MyURLValue</c> →
    /// <c>my_url_value</c>). An uppercase letter starts a word after a
    /// lowercase letter, a digit or a space, and ends a run of uppercase
    /// letters when a lowercase letter follows it. Spaces collapse into one
    /// separator between words and are dropped at either end; every other
    /// character is written as is and starts over.
    /// </summary>
    private static string Separate(string name, char separator, bool lowercase)
    {
        var builder = new StringBuilder(name.Length + name.Length / 5);
        var state = SeparatorState.NotStarted;

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            switch (char.GetUnicodeCategory(current))
            {
                case UnicodeCategory.UppercaseLetter:
                    if (StartsWordAt(name, i, state))
                        builder.Append(separator);

                    builder.Append(lowercase ? char.ToLowerInvariant(current) : current);
                    state = SeparatorState.UppercaseLetter;
                    break;

                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.DecimalDigitNumber:
                    if (state == SeparatorState.SpaceSeparator)
                        builder.Append(separator);

                    builder.Append(lowercase ? current : char.ToUpperInvariant(current));
                    state = SeparatorState.LowercaseLetterOrDigit;
                    break;

                case UnicodeCategory.SpaceSeparator:
                    if (state != SeparatorState.NotStarted)
                        state = SeparatorState.SpaceSeparator;

                    break;

                default:
                    builder.Append(current);
                    state = SeparatorState.NotStarted;
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the uppercase letter at <paramref name="index"/> starts a new
    /// word: after a lowercase letter, a digit or a space, or as the last
    /// letter of an uppercase run that a lowercase letter follows
    /// (<c>XMLReader</c> → <c>xml_reader</c>).
    /// </summary>
    private static bool StartsWordAt(string name, int index, SeparatorState state) =>
        state switch
        {
            SeparatorState.LowercaseLetterOrDigit or SeparatorState.SpaceSeparator => true,
            SeparatorState.UppercaseLetter => index + 1 < name.Length
                && char.IsLower(name[index + 1]),
            _ => false,
        };

    private enum SeparatorState
    {
        NotStarted,
        UppercaseLetter,
        LowercaseLetterOrDigit,
        SpaceSeparator,
    }
}
