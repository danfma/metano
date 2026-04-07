namespace MetaSharp.TypeScript.AST;

public sealed record TsUnionType(IReadOnlyList<TsType> Types) : TsType;
