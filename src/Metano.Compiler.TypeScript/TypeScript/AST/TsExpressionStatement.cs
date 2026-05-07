namespace Metano.Compiler.TypeScript.AST;

public sealed record TsExpressionStatement(TsExpression Expression) : TsStatement;
