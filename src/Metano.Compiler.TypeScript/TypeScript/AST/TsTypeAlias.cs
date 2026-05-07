namespace Metano.Compiler.TypeScript.AST;

public sealed record TsTypeAlias(
    string Name,
    TsType Type,
    bool Exported = true,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null
) : TsTopLevel;
