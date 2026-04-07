using System.Text;

namespace MetaSharp.TypeScript;

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

    public void WriteQuoted(string value)
    {
        Write("\"");
        Write(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
        Write("\"");
    }

    /// <summary>
    /// Writes a comma-separated list using a callback for each item.
    /// </summary>
    public void WriteList<T>(IReadOnlyList<T> items, Action<T> writeItem, string separator = ", ")
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) Write(separator);
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
}
