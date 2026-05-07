namespace Metano.Compiler.TypeScript.AST;

public sealed record TsIfStatement(
    TsExpression Condition,
    IReadOnlyList<TsStatement> Then,
    IReadOnlyList<TsStatement>? Else = null
) : TsStatement;
