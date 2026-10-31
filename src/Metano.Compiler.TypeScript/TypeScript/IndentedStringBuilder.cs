using System.Text;

namespace Metano.Compiler.TypeScript;

/// <summary>
/// A StringBuilder wrapper that tracks indentation level and auto-indents on new lines.
/// </summary>
public sealed class IndentedStringBuilder
{
    private readonly StringBuilder _sb = new();
    private readonly string _indentStr;
    private int _indent;
    private bool _lineStart = true;

    public IndentedStringBuilder(string indent = "  ")
    {
        _indentStr = indent;
    }

    public void Clear()
    {
        _sb.Clear();
        _indent = 0;
        _lineStart = true;
    }

    public void Indent() => _indent++;

    public void Dedent() => _indent--;

    public void Write(string text)
    {
        if (_lineStart)
        {
            for (var i = 0; i < _indent; i++)
                _sb.Append(_indentStr);
            _lineStart = false;
        }

        _sb.Append(text);
    }

    public void WriteLn()
    {
        _sb.AppendLine();
        _lineStart = true;
    }

    /// <summary>
    /// Writes <paramref name="value"/> as a double-quoted JS string literal,
    /// escaping the backslash, the quote and every control or line-terminator
    /// character, so a C# value such as <c>"a\nb"</c> or <c>'\0'</c> prints as
    /// a valid, visible escape instead of a raw character. Other control
    /// characters (NUL included) print as <c>\uXXXX</c>, which stays valid
    /// whatever character follows (<c>\0</c> followed by a digit would be a
    /// legacy octal escape).
    /// </summary>
    public void WriteQuoted(string value)
    {
        Write("\"");
        Write(EscapeStringLiteral(value));
        Write("\"");
    }

    /// <summary>
    /// Writes <paramref name="value"/>, the cooked (runtime) text of one
    /// template-literal quasi, escaped for a backtick template: the
    /// backslash, the backtick, a <c>$</c> that would open a <c>${</c> hole
    /// and every control or line-terminator character print as escapes, so
    /// the quasi evaluates back to exactly <paramref name="value"/>. A raw
    /// carriage return would otherwise be normalized to a line feed by the
    /// template-literal grammar.
    /// </summary>
    public void WriteTemplateText(string value)
    {
        var escaped = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            escaped.Append(EscapeTemplateChar(value, index));
        }

        Write(escaped.ToString());
    }

    private static string EscapeTemplateChar(string value, int index) =>
        value[index] switch
        {
            '`' => "\\`",
            '$' when OpensTemplateHole(value, index) => "\\$",
            var c => EscapeNonDelimiterChar(c),
        };

    private static bool OpensTemplateHole(string value, int index) =>
        index + 1 < value.Length && value[index + 1] == '{';

    private static string EscapeStringLiteral(string value)
    {
        var escaped = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            escaped.Append(c == '"' ? "\\\"" : EscapeNonDelimiterChar(c));
        }

        return escaped.ToString();
    }

    /// <summary>
    /// The escape every JS string form shares: the backslash and the control
    /// or line-terminator characters. Each caller escapes its own delimiter.
    /// </summary>
    private static string EscapeNonDelimiterChar(char c) =>
        c switch
        {
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ when char.IsControl(c) || c is '\u2028' or '\u2029' => $"\\u{(int)c:x4}",
            _ => c.ToString(),
        };

    /// <summary>
    /// Writes a comma-separated list using a callback for each item.
    /// </summary>
    public void WriteList<T>(IReadOnlyList<T> items, Action<T> writeItem, string separator = ", ")
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
                Write(separator);
            writeItem(items[i]);
        }
    }

    /// <summary>
    /// Writes each item on its own line with current indentation.
    /// </summary>
    public void WriteLines<T>(IReadOnlyList<T> items, Action<T> writeItem)
    {
        foreach (var item in items)
        {
            writeItem(item);
            WriteLn();
        }
    }

    /// <summary>
    /// Writes a block: opens brace, indents, writes body, dedents, closes brace.
    /// </summary>
    public void WriteBlock(Action writeBody)
    {
        Write(" {");
        WriteLn();
        Indent();
        writeBody();
        Dedent();
        Write("}");
    }

    /// <summary>
    /// Writes an empty block: " { }"
    /// </summary>
    public void WriteEmptyBlock()
    {
        Write(" { }");
    }

    public override string ToString() => _sb.ToString().TrimEnd('\n') + "\n";

    /// <summary>
    /// Returns the buffered text verbatim, with no trailing-newline
    /// normalization. Used when embedding a fragment (e.g. a single
    /// type or expression rendered for inclusion in a synthesized raw
    /// statement) — fragments must not carry a trailing newline that
    /// the surrounding context would not have produced on its own.
    /// </summary>
    public string GetRawString() => _sb.ToString();
}
