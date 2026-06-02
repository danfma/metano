namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript array-destructuring variable declaration —
/// <c>const [a, , b] = init;</c>. A null entry in <see cref="Names"/> is a
/// discard hole: the printer emits an empty slot so the position is skipped and
/// nothing is bound there. Lowered from <c>Metano.Compiler.IR.IrTupleDeconstruction</c>.
/// </summary>
/// <param name="Names">Bound names in positional order; null = discard hole.</param>
/// <param name="Initializer">The right-hand side producing the array/tuple value.</param>
/// <param name="Const">When true emits <c>const</c>; otherwise <c>let</c>.</param>
/// <param name="Exported">When true, prefixes the declaration with <c>export</c>.</param>
public sealed record TsDestructuringDeclaration(
    IReadOnlyList<string?> Names,
    TsExpression Initializer,
    bool Const = true,
    bool Exported = false
) : TsStatement;
