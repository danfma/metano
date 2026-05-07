namespace Metano.Compiler.TypeScript.AST;

public sealed record TsPropertyAccess(TsExpression Object, string Property) : TsExpression;
