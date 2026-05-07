namespace Metano.Compiler.TypeScript.AST;

public sealed record TsUnaryExpression(string Operator, TsExpression Operand) : TsExpression;
