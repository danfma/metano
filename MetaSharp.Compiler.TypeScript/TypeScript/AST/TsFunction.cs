namespace MetaSharp.TypeScript.AST;

public sealed record TsFunction(
    string Name,
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType,
    IReadOnlyList<TsStatement> Body,
    bool Exported = true,
    bool Async = false,
    bool Generator = false,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null
) : TsTopLevel;
