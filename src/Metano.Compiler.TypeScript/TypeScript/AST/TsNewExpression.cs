namespace Metano.Compiler.TypeScript.AST;

public sealed record TsNewExpression(TsExpression Callee, IReadOnlyList<TsExpression> Arguments)
    : TsExpression;
