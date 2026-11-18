namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A <c>try</c> statement: <c>try { … } catch (e) { … } finally { … }</c>. JavaScript
/// has one untyped <c>catch</c>, so the typed clauses of a C# <c>try</c> arrive here
/// already lowered to a single <see cref="TsCatchClause"/> (spec 011 FR-015).
/// </summary>
/// <param name="Body">The statements of the <c>try</c> block.</param>
/// <param name="Catch">The <c>catch</c> block; <c>null</c> for <c>try</c> / <c>finally</c>.</param>
/// <param name="Finally">The statements of the <c>finally</c> block; <c>null</c> when
/// there is none.</param>
public sealed record TsTryStatement(
    IReadOnlyList<TsStatement> Body,
    TsCatchClause? Catch,
    IReadOnlyList<TsStatement>? Finally
) : TsStatement;
