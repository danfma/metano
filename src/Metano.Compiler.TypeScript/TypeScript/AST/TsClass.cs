namespace Metano.Compiler.TypeScript.AST;

public sealed record TsClass(
    string Name,
    TsConstructor? Constructor,
    IReadOnlyList<TsClassMember> Members,
    bool Exported = true,
    TsType? Extends = null,
    IReadOnlyList<TsType>? Implements = null,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null,
    bool Abstract = false
) : TsTopLevel;
