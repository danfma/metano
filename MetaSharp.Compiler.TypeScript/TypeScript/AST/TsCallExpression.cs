namespace MetaSharp.TypeScript.AST;

public sealed record TsCallExpression(TsExpression Callee, IReadOnlyList<TsExpression> Arguments)
    : TsExpression;
