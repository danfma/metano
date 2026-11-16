namespace Metano.Compiler.Dart;

/// <summary>
/// The text of a single-quoted Dart string literal, shared by every path that
/// prints one (string and <c>char</c> literals, interpolation text, enum
/// values): a backslash, a quote and the line breaks are escaped, and so is
/// <c>$</c>, which Dart would otherwise read as an interpolation.
/// </summary>
public static class DartStringLiteral
{
    public static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("$", "\\$")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
}
