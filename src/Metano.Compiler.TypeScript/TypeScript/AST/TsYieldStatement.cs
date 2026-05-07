namespace Metano.Compiler.TypeScript.AST;

public sealed record TsYieldStatement(TsExpression Expression) : TsStatement;
