namespace MetaSharp.TypeScript.AST;

public sealed record TsGetterMember(string Name, TsType ReturnType, IReadOnlyList<TsStatement> Body)
    : TsClassMember;
