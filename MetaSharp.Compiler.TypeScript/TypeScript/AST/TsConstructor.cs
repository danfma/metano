namespace MetaSharp.TypeScript.AST;

public sealed record TsConstructor(
    IReadOnlyList<TsConstructorParam> Parameters,
    IReadOnlyList<TsStatement> Body,
    IReadOnlyList<TsConstructorOverload>? Overloads = null
);

/// <summary>
/// An overload signature for a constructor: constructor(x: number, y: number);
/// </summary>
public sealed record TsConstructorOverload(
    IReadOnlyList<TsConstructorParam> Parameters
);
