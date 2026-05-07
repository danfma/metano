namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// Postfix unary expression — the operator follows the operand (<c>x++</c>, <c>x--</c>).
/// Distinct from <see cref="TsUnaryExpression"/> which always renders the operator on
/// the left.
/// </summary>
public sealed record TsPostfixUnaryExpression(TsExpression Operand, string Operator) : TsExpression;
