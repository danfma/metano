namespace Metano.Compiler.TypeScript.AST;

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
    IReadOnlyList<TsMethodOverload>? Overloads = null,
    bool Abstract = false,
    bool Override = false
) : TsClassMember;

/// <summary>
/// An overload signature for a method: methodName&lt;T&gt;(param: Type): ReturnType;
/// <paramref name="TypeParameters"/> carries the overload's own method type
/// parameters, printed per signature.
/// </summary>
public sealed record TsMethodOverload(
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType,
    IReadOnlyList<TsTypeParameter>? TypeParameters = null
);

public enum TsAccessibility
{
    Public,
    Protected,
    Private,
    None,
}
