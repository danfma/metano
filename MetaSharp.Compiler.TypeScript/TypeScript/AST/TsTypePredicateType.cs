namespace MetaSharp.TypeScript.AST;

/// <summary>
/// Represents a TypeScript type predicate: "value is TypeName"
/// Used as return type of type guard functions.
/// </summary>
public sealed record TsTypePredicateType(string ParameterName, TsType Type) : TsType;
