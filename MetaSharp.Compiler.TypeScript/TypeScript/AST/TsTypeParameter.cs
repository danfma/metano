namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A type parameter declaration: T, or T extends Entity.
/// </summary>
public sealed record TsTypeParameter(string Name, TsType? Constraint = null);
