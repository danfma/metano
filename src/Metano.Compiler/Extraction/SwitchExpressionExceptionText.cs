namespace Metano.Compiler.Extraction;

/// <summary>
/// .NET's texts for a <c>SwitchExpressionException</c>, shared by the
/// extraction (its default message) and every target that writes the
/// unmatched value into the message itself.
/// </summary>
public static class SwitchExpressionExceptionText
{
    /// <summary>The exception's default message.</summary>
    public const string DefaultMessage =
        "Non-exhaustive switch expression failed to match its input.";

    /// <summary>
    /// The line .NET appends for the value no switch arm matched, before the
    /// value and a closing <c>.</c>.
    /// </summary>
    public const string UnmatchedValueLine = "\nUnmatched value was ";
}
