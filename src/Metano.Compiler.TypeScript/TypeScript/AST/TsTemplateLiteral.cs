namespace Metano.Compiler.TypeScript.AST;

public sealed record TsTemplateLiteral(
    IReadOnlyList<string> Quasis,
    IReadOnlyList<TsExpression> Expressions
) : TsExpression;
