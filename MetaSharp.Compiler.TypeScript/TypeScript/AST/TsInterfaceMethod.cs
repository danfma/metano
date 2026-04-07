namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A method signature in a TypeScript interface: methodName(param: Type): ReturnType;
/// </summary>
public sealed record TsInterfaceMethod(
    string Name,
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null
);
