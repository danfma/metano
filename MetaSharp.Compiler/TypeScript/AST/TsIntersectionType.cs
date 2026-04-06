namespace MetaSharp.TypeScript.AST;

public sealed record TsIntersectionType(IReadOnlyList<TsType> Types) : TsType;
