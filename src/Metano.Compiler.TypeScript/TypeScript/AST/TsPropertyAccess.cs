namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A member access: <c>object.property</c>, or the optional-chaining form
/// <c>object?.property</c> when <paramref name="Optional"/> is <c>true</c>.
/// <paramref name="ContinuesChain"/> marks a plain <c>.property</c> step that
/// belongs to an enclosing optional chain (the <c>.c</c> in <c>a?.b.c</c>):
/// the chain keeps short-circuiting through it, so the printer must not
/// close the chain with parentheses before it.
/// </summary>
public sealed record TsPropertyAccess(
    TsExpression Object,
    string Property,
    bool Optional = false,
    bool ContinuesChain = false
) : TsExpression;
