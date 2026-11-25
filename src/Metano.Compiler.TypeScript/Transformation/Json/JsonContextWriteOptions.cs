namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The text formatting a context's <c>[JsonSourceGenerationOptions]</c> sets,
/// mirrored by the runtime <c>JsonWriteOptions</c> that <c>serializeText</c>
/// reads (spec 009 FR-050). The indent members only matter when
/// <paramref name="WriteIndented"/> is <c>true</c>, as in System.Text.Json.
/// </summary>
/// <param name="WriteIndented">The text is written over several lines.</param>
/// <param name="IndentCharacter">A space or a tab; a space by default.</param>
/// <param name="IndentSize">The indent characters per nesting level; 2 by default.</param>
/// <param name="NewLine">The line break, <c>"\n"</c> or <c>"\r\n"</c>.</param>
internal sealed record JsonContextWriteOptions(
    bool WriteIndented,
    char IndentCharacter,
    int IndentSize,
    string NewLine
)
{
    public const char DefaultIndentCharacter = ' ';

    public const int DefaultIndentSize = 2;

    /// <summary>
    /// The line break the runtime writes when none is set: the <c>"\n"</c> System.Text.Json
    /// writes on Linux and macOS (its default is <c>Environment.NewLine</c>, so
    /// <c>"\r\n"</c> on Windows).
    /// </summary>
    public const string DefaultNewLine = "\n";

    /// <summary>The largest indent size System.Text.Json accepts.</summary>
    public const int MaxIndentSize = 127;

    public static readonly JsonContextWriteOptions Default = new(
        WriteIndented: false,
        IndentCharacter: DefaultIndentCharacter,
        IndentSize: DefaultIndentSize,
        NewLine: DefaultNewLine
    );

    public static bool IsValidIndentCharacter(char value) => value is ' ' or '\t';

    public static bool IsValidIndentSize(int value) => value is >= 0 and <= MaxIndentSize;

    public static bool IsValidNewLine(string? value) => value is "\n" or "\r\n";
}
