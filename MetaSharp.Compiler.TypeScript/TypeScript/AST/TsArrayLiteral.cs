namespace MetaSharp.TypeScript.AST;

/// <summary>
/// An array literal expression: [expr1, expr2, ...]
/// </summary>
public sealed record TsArrayLiteral(IReadOnlyList<TsExpression> Elements) : TsExpression;
