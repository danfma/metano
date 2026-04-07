namespace MetaSharp.TypeScript.AST;

/// <summary>
/// Represents a TypeScript tuple type: [T1, T2, ...]
/// </summary>
public sealed record TsTupleType(IReadOnlyList<TsType> Elements) : TsType;
