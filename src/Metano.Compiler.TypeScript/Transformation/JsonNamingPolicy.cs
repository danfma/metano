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

    public static string SnakeCaseLower(string name) =>
        string.Join('_', SplitWords(name)).ToLowerInvariant();

    public static string SnakeCaseUpper(string name) =>
        string.Join('_', SplitWords(name)).ToUpperInvariant();

    public static string KebabCaseLower(string name) =>
        string.Join('-', SplitWords(name)).ToLowerInvariant();

    public static string KebabCaseUpper(string name) =>
        string.Join('-', SplitWords(name)).ToUpperInvariant();

    /// <summary>
    /// Splits a PascalCase or camelCase name into words.
    /// Handles uppercase transitions, acronyms, and numbers.
    /// </summary>
    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        var start = 0;

        for (var i = 1; i < name.Length; i++)
        {
            var ch = name[i];
            var prev = name[i - 1];

            if (char.IsUpper(ch) && !char.IsUpper(prev))
            {
                // lowercase→UPPER transition: "firstName" → "first", "Name"
                words.Add(name[start..i]);
                start = i;
            }
            else if (
                char.IsUpper(ch)
                && i + 1 < name.Length
                && !char.IsUpper(name[i + 1])
                && !char.IsDigit(name[i + 1])
            )
            {
                // End of acronym: "HTMLParser" → "HTML", "Parser"
                words.Add(name[start..i]);
                start = i;
            }
            else if (char.IsDigit(ch) && !char.IsDigit(prev))
            {
                words.Add(name[start..i]);
                start = i;
            }
            else if (!char.IsDigit(ch) && char.IsDigit(prev))
            {
                words.Add(name[start..i]);
                start = i;
            }
        }

        if (start < name.Length)
            words.Add(name[start..]);

        return words;
    }
}
