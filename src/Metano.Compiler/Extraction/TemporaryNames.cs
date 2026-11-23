namespace Metano.Compiler.Extraction;

/// <summary>
/// Allocates the synthesized temporary names of one body (<c>receiver$temp$N</c>,
/// <c>tuple$temp$N</c>, <c>element$temp$N</c>) from one counter, so every
/// extractor lowering part of that body draws distinct names and a nested binding
/// never shadows an enclosing one.
/// </summary>
internal sealed class TemporaryNames
{
    private int _next;

    /// <summary><paramref name="prefix"/> followed by the next free index.</summary>
    public string Next(string prefix) => $"{prefix}{_next++}";
}
