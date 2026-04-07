namespace MetaSharp.TypeScript.AST;

public sealed record TsNamedType(string Name, IReadOnlyList<TsType>? TypeArguments = null) : TsType;
