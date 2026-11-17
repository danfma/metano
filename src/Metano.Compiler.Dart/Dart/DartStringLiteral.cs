using System.Text;

namespace Metano.Compiler.Dart;

/// <summary>
/// The text of a single-quoted Dart string literal, shared by every path that
/// prints one (string and <c>char</c> literals, interpolation text, enum
/// values): a backslash, a quote and the line breaks are escaped, and so is
/// <c>$</c>, which Dart would otherwise read as an interpolation. An unpaired
/// UTF-16 surrogate prints as <c>\uXXXX</c>, so the file is valid UTF-8; a
/// valid pair stays its character (spec 001 FR-073).
/// </summary>
public static class DartStringLiteral
{
    public static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            escaped.Append(EscapeUnit(text, index));
        }

        return escaped.ToString();
    }

    private static string EscapeUnit(string text, int index) =>
        text[index] switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '$' => "\\$",
            '\n' => "\\n",
            '\r' => "\\r",
            var unit when SurrogateText.IsLone(text, index) => $"\\u{(int)unit:X4}",
            var unit => unit.ToString(),
        };
}
