namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A member access: <c>object.property</c>, or the optional-chaining form
/// <c>object?.property</c> when <paramref name="Optional"/> is <c>true</c>.
/// </summary>
public sealed record TsPropertyAccess(TsExpression Object, string Property, bool Optional = false)
    : TsExpression;
