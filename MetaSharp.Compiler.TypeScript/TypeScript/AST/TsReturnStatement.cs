namespace MetaSharp.TypeScript.AST;

public sealed record TsReturnStatement(TsExpression? Expression = null) : TsStatement;
