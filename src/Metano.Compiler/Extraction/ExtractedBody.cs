using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// One extracted body: its statements, plus the local-binding renames and
/// unresolvable collisions of the scope they were extracted from (#398) — a
/// value per body, never extractor state a later body could inherit.
/// </summary>
public sealed record ExtractedBody(
    IReadOnlyList<IrStatement> Statements,
    LocalBindingNames LocalNames
);
