namespace Metano.Compiler.TypeScript.AST;

public sealed record TsIntersectionType(IReadOnlyList<TsType> Types) : TsType;
