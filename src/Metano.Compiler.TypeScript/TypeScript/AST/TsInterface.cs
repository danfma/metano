namespace Metano.TypeScript.AST;

public sealed record TsInterface(
    string Name,
    IReadOnlyList<TsProperty> Properties,
    bool Exported = true,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null,
    IReadOnlyList<TsInterfaceMethod>? Methods = null,
    IReadOnlyList<TsType>? Extends = null
) : TsTopLevel;
