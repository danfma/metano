using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Records where an extracted statement came from (<see cref="IrStatement.Span"/>),
/// so a target that renders it later can report at the C# source (W9-13a).
/// </summary>
internal static class IrStatementSpans
{
    /// <summary>
    /// <paramref name="statement"/> with the span of <paramref name="source"/>,
    /// unless a nested extraction already gave it one.
    /// </summary>
    public static IrStatement WithSpanOf(SyntaxNode source, IrStatement statement) =>
        statement.Span is null
            ? statement with
            {
                Span = IrSourceSpanLocations.From(source.GetLocation()),
            }
            : statement;

    /// <summary>
    /// The statements <paramref name="source"/> lowered to, each with its span
    /// when it has none (the bindings a lowering hoists ahead of it).
    /// </summary>
    public static IEnumerable<IrStatement> Inherit(
        IrStatement source,
        IEnumerable<IrStatement> lowered
    ) =>
        source.Span is null
            ? lowered
            : lowered.Select(statement =>
                statement.Span is null ? statement with { Span = source.Span } : statement
            );
}
