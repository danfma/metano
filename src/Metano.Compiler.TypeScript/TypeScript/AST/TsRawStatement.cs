namespace Metano.TypeScript.AST;

/// <summary>
/// Escape hatch for statements the AST doesn't model yet (for / while /
/// do-while / foreach / try). The printer emits <see cref="Text"/> verbatim
/// at the current indentation and appends a newline — useful while keeping
/// C# constructs alive through the TS pipeline without bloating the AST
/// with nodes that have no Metano-level semantics the rest of the
/// compiler needs to reason about.
/// </summary>
/// <param name="Text">Raw TS source line (without trailing newline or
/// leading indent — the printer handles both).</param>
public sealed record TsRawStatement(string Text) : TsStatement;
