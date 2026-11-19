namespace Metano.Compiler.IR;

/// <summary>
/// Where an IR node came from in the C# source, for a target that reports a
/// diagnostic at the node while it lowers it (a shape it cannot render in that
/// position). <see cref="Start"/> and <see cref="Length"/> are the character
/// span in <see cref="FilePath"/>; the lines and columns are zero-based.
/// </summary>
public sealed record IrSourceLocation(
    string FilePath,
    int Start,
    int Length,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn
);
