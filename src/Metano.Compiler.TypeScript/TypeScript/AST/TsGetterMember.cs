namespace Metano.Compiler.TypeScript.AST;

public sealed record TsGetterMember(
    string Name,
    TsType ReturnType,
    IReadOnlyList<TsStatement> Body,
    bool Static = false
) : TsClassMember;
