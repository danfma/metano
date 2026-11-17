namespace Metano.Compiler;

/// <summary>
/// UTF-16 facts every target's string printing shares (spec 001 FR-073): a
/// generated file is UTF-8, which cannot hold an unpaired surrogate, so a
/// literal prints it as an escape.
/// </summary>
public static class SurrogateText
{
    /// <summary>
    /// Whether the UTF-16 unit at <paramref name="index"/> of <paramref name="text"/>
    /// is a surrogate with no partner: a high one not followed by a low one, or a
    /// low one not preceded by a high one. A valid pair is not lone.
    /// </summary>
    public static bool IsLone(string text, int index)
    {
        var unit = text[index];

        if (char.IsHighSurrogate(unit))
            return index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]);

        return char.IsLowSurrogate(unit) && (index == 0 || !char.IsHighSurrogate(text[index - 1]));
    }
}
