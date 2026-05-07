namespace Metano.Compiler.TypeScript.AST;

public sealed record TsThrowStatement(TsExpression Expression) : TsStatement;
