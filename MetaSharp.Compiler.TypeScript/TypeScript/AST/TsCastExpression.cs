namespace MetaSharp.TypeScript.AST;

public sealed record TsCastExpression(TsExpression Expression, TsType Type) : TsExpression;
