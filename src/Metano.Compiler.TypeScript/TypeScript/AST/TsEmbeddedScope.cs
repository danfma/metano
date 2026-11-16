namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A lexical scope inside the raw text of a <see cref="TsRawStatement"/>: the names its
/// text binds (a <c>foreach</c> variable, a <c>catch</c> parameter) and the nodes the
/// text renders inside that binding. A walker that resolves identifiers (the import
/// collector) treats those names as local within <see cref="Statements"/> and
/// <see cref="Expressions"/>, so a local never resolves to an export of another module.
/// Never printed.
/// </summary>
/// <param name="BoundNames">The names the raw text declares for this scope, beyond the
/// ones its <paramref name="Statements"/> declare themselves.</param>
/// <param name="Statements">The statements rendered inside the scope.</param>
/// <param name="Expressions">The expressions rendered inside the scope (a <c>for</c>
/// condition and incrementors, which read the loop's own declarations).</param>
public sealed record TsEmbeddedScope(
    IReadOnlyList<string> BoundNames,
    IReadOnlyList<TsStatement> Statements,
    IReadOnlyList<TsExpression>? Expressions = null
)
{
    public IReadOnlyList<TsExpression> ScopedExpressions => Expressions ?? [];
}
