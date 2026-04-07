namespace MetaSharp.TypeScript.AST;

public sealed record TsMethodMember(
    string Name,
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType,
    IReadOnlyList<TsStatement> Body,
    bool Static = false,
    bool Async = false,
    bool Generator = false,
    TsAccessibility Accessibility = TsAccessibility.Public,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null,
    IReadOnlyList<TsMethodOverload>? Overloads = null
) : TsClassMember;

/// <summary>
/// An overload signature for a method: methodName(param: Type): ReturnType;
/// </summary>
public sealed record TsMethodOverload(
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType
);

public enum TsAccessibility
{
    Public,
    Protected,
    Private,
    None,
}
