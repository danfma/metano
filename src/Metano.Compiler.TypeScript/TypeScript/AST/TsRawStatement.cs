namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// Escape hatch for statements the AST doesn't model yet (for / while /
/// do-while / foreach). The printer emits <see cref="Text"/> verbatim
/// at the current indentation and appends a newline — useful while keeping
/// C# constructs alive through the TS pipeline without bloating the AST
/// with nodes that have no Metano-level semantics the rest of the
/// compiler needs to reason about.
/// </summary>
/// <param name="Text">Raw TS source line (without trailing newline or
/// leading indent — the printer handles both).</param>
public sealed record TsRawStatement(string Text) : TsStatement
{
    /// <summary>
    /// The statements <see cref="Text"/> renders (a loop body, the
    /// declarations of a <c>for</c> header), so AST
    /// walkers such as the import collector still see the identifiers,
    /// types and runtime helpers they reference. Never printed.
    /// </summary>
    public IReadOnlyList<TsStatement> EmbeddedStatements { get; init; } = [];

    /// <summary>
    /// The expressions <see cref="Text"/> renders outside
    /// <see cref="EmbeddedStatements"/> (a loop condition, a <c>for</c>
    /// incrementor, a <c>foreach</c> collection). Never printed.
    /// </summary>
    public IReadOnlyList<TsExpression> EmbeddedExpressions { get; init; } = [];

    /// <summary>
    /// The parts of <see cref="Text"/> that run inside a name the text binds itself
    /// (a <c>foreach</c> body), apart from
    /// <see cref="EmbeddedStatements"/> and <see cref="EmbeddedExpressions"/>, which
    /// bind nothing. Never printed.
    /// </summary>
    public IReadOnlyList<TsEmbeddedScope> EmbeddedScopes { get; init; } = [];
}
