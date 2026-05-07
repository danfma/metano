namespace Metano.Compiler.TypeScript.AST;

public sealed record TsUnionType(IReadOnlyList<TsType> Types) : TsType;
