namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript destructuring variable declaration — <c>const [a, , b] = init;</c>,
/// <c>const { x: a, y: b } = init;</c>, nested patterns included. Lowered from
/// <c>Metano.Compiler.IR.IrTupleDeconstruction</c>.
/// </summary>
/// <param name="Pattern">The array or object pattern bound.</param>
/// <param name="Initializer">The right-hand side producing the destructured value.</param>
/// <param name="Const">When true emits <c>const</c>; otherwise <c>let</c>.</param>
/// <param name="Exported">When true, prefixes the declaration with <c>export</c>.</param>
public sealed record TsDestructuringDeclaration(
    TsBindingTarget Pattern,
    TsExpression Initializer,
    bool Const = true,
    bool Exported = false
) : TsStatement
{
    /// <summary>The names the declaration binds, nested ones included.</summary>
    public IEnumerable<string> BoundNames => Pattern.BoundNames;
}
