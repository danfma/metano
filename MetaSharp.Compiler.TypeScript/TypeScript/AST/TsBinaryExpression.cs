namespace MetaSharp.TypeScript.AST;

public sealed record TsBinaryExpression(TsExpression Left, string Operator, TsExpression Right)
    : TsExpression;
