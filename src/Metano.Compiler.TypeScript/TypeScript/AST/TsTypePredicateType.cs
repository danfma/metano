namespace Metano.TypeScript.AST;

/// <summary>
/// Represents a TypeScript type predicate used as the return type of a
/// type guard function. Two shapes:
/// <list type="bullet">
///   <item><c>value is TypeName</c> — the classic <c>isT</c> guard:
///     returns <c>boolean</c>, narrows the parameter when
///     <c>true</c>.</item>
///   <item><c>asserts value is TypeName</c> — the throwing
///     <c>assertT</c> variant: returns <c>void</c>, narrows the
///     parameter unconditionally after the call (if the function
///     returns normally, the value was a <c>TypeName</c>).</item>
/// </list>
/// </summary>
public sealed record TsTypePredicateType(string ParameterName, TsType Type, bool IsAsserts = false)
    : TsType;
