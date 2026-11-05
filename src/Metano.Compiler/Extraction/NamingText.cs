namespace Metano.Compiler.Extraction;

/// <summary>Identifier spelling shared by the overload naming pieces.</summary>
internal static class NamingText
{
    /// <summary>The text with its first letter upper-cased (<c>width</c> → <c>Width</c>).</summary>
    public static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
