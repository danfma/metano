namespace MetaSharp.TypeScript.AST;

public sealed record TsArrowFunction(
    IReadOnlyList<TsParameter> Parameters,
    IReadOnlyList<TsStatement> Body,
    bool Async = false
) : TsExpression;
