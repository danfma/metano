using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Compile-time ports of the System.Text.Json naming policies, used to
/// pre-compute JSON wire names during transpilation. They walk UTF-16 code
/// units as .NET does, so a surrogate half is no letter.
/// </summary>
internal static class JsonNamingPolicy
{
    /// <summary>
    /// The policy of a <c>JsonKnownNamingPolicy</c> value; <c>null</c> for
    /// <c>Unspecified</c> or an undefined value (names stay as declared).
    /// </summary>
    public static Func<string, string>? FromKnownPolicy(JsonKnownNamingPolicy policy) =>
        policy switch
        {
            JsonKnownNamingPolicy.CamelCase => CamelCase,
            JsonKnownNamingPolicy.SnakeCaseLower => SnakeCaseLower,
            JsonKnownNamingPolicy.SnakeCaseUpper => SnakeCaseUpper,
            JsonKnownNamingPolicy.KebabCaseLower => KebabCaseLower,
            JsonKnownNamingPolicy.KebabCaseUpper => KebabCaseUpper,
            _ => null,
        };

    /// <summary>
    /// A port of System.Text.Json's <c>JsonCamelCaseNamingPolicy</c>: lowercases
    /// the leading uppercase run, keeping its last letter when a non-uppercase
    /// character follows it (measured on .NET 10: <c>URL</c> → <c>url</c>,
    /// <c>IOStream</c> → <c>ioStream</c>, <c>ABC_D</c> → <c>abC_D</c>,
    /// <c>Item_Name</c> → <c>item_Name</c>); a name that does not start with an
    /// uppercase letter (<c>_Leading</c>) is kept.
    /// </summary>
    public static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            return name;

        var chars = name.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;

            var hasNext = i + 1 < chars.Length;

            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (chars[i + 1] == ' ')
                    chars[i] = char.ToLowerInvariant(chars[i]);

                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    public static string SnakeCaseLower(string name) => Separate(name, '_', lowercase: true);

    public static string SnakeCaseUpper(string name) => Separate(name, '_', lowercase: false);

    public static string KebabCaseLower(string name) => Separate(name, '-', lowercase: true);

    public static string KebabCaseUpper(string name) => Separate(name, '-', lowercase: false);

    /// <summary>
    /// A port of System.Text.Json's <c>JsonSeparatorNamingPolicy</c> (measured
    /// on .NET 10: <c>Item_Name</c> → <c>item_name</c>, <c>Value2</c> →
    /// <c>value2</c>, <c>A1B</c> → <c>a1_b</c>, <c>MyURLValue</c> →
    /// <c>my_url_value</c>). An uppercase letter starts a word after a
    /// lowercase letter, a digit or a space, and ends a run of uppercase
    /// letters when a lowercase letter follows it. Spaces collapse into one
    /// separator between words and are dropped at either end. Any other
    /// character (<c>_</c>, a surrogate half) is written as is, and the letter
    /// after it never gets a separator.
    /// </summary>
    private static string Separate(string name, char separator, bool lowercase)
    {
        // Sized as .NET sizes its buffer: 1.2 × the name, one separator per five characters.
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
    private static bool StartsWordAt(string name, int index, SeparatorState previousState) =>
        previousState switch
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
