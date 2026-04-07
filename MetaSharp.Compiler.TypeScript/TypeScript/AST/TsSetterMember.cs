namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A TypeScript setter: set name(value: Type) { ... }
/// </summary>
public sealed record TsSetterMember(
    string Name,
    TsParameter ValueParam,
    IReadOnlyList<TsStatement> Body
) : TsClassMember;
