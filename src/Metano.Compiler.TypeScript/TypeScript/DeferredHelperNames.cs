namespace Metano.Compiler.TypeScript;

/// <summary>
/// The deferred spelling of a runtime helper reference. Wrapping exists only
/// because the fragment bakers (<c>IrToTsStatementBridge.WrapLoop</c> headers
/// and <see cref="Printer.PrintExpressionFragment"/>) stringify before the
/// import collector has decided a module's helper aliases — every wrap
/// carries the plain name inside private-use markers the printer never
/// otherwise emits, and the final <see cref="Printer.Print"/> pass resolves
/// each one to the file's alias (issue #484) or back to the plain name.
/// </summary>
internal static class DeferredHelperNames
{
    /// <summary>Opens a deferred helper name; private-use, never in emitted source.</summary>
    internal const char Open = '';

    /// <summary>Closes a deferred helper name; private-use, never in emitted source.</summary>
    internal const char Close = '';

    /// <summary>The deferred form of <paramref name="name"/> for fragment output.</summary>
    internal static string Wrap(string name) => $"{Open}{name}{Close}";

    /// <summary>
    /// Replaces every wrapped helper name in <paramref name="text"/> with the
    /// file's alias, or the plain name when the module has no alias for it.
    /// </summary>
    internal static string Resolve(string text, IReadOnlyDictionary<string, string>? aliases)
    {
        if (!text.Contains(Open))
            return text;

        var builder = new System.Text.StringBuilder(text.Length);
        var rest = text.AsSpan();

        while (true)
        {
            var start = rest.IndexOf(Open);

            if (start < 0)
            {
                builder.Append(rest);
                break;
            }

            builder.Append(rest[..start]);
            rest = rest[(start + 1)..];
            var end = rest.IndexOf(Close);

            if (end < 0)
            {
                // Unpaired open marker: nothing to resolve, keep the text.
                builder.Append(Open);
                builder.Append(rest);
                break;
            }

            var name = rest[..end].ToString();
            builder.Append(aliases?.GetValueOrDefault(name) ?? name);
            rest = rest[(end + 1)..];
        }

        return builder.ToString();
    }
}
