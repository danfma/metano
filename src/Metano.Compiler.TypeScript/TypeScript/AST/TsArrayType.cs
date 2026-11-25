namespace Metano.Compiler.TypeScript.AST;

public sealed record TsArrayType(TsType ElementType, bool IsReadonly = false) : TsType;
