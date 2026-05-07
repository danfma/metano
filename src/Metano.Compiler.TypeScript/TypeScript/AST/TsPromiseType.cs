namespace Metano.Compiler.TypeScript.AST;

public sealed record TsPromiseType(TsType Inner) : TsType;
