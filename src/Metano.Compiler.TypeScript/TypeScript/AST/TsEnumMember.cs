namespace Metano.Compiler.TypeScript.AST;

public sealed record TsEnumMember(string Name, TsExpression? Value = null);
