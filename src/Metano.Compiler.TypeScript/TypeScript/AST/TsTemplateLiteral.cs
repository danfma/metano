namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A backtick template literal. <see cref="Quasis"/> hold the cooked (runtime)
/// text of each literal segment, never source-escaped text: the printer
/// escapes them, so producers pass the value the segment must evaluate to.
/// </summary>
public sealed record TsTemplateLiteral(
    IReadOnlyList<string> Quasis,
    IReadOnlyList<TsExpression> Expressions
) : TsExpression;
