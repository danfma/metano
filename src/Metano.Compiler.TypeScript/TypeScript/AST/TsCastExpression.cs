namespace Metano.Compiler.TypeScript.AST;

public sealed record TsCastExpression(TsExpression Expression, TsType Type) : TsExpression;
