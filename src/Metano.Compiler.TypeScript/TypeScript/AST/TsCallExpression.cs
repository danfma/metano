namespace Metano.TypeScript.AST;

/// <summary>
/// A TypeScript call expression: <c>callee(args)</c>. When
/// <paramref name="Optional"/> is <c>true</c>, the printer emits
/// the optional-call shape <c>callee?.(args)</c> — used for
/// delegate-typed null-conditional invocations
/// (<c>handler?.Invoke(args)</c> in C# →
/// <c>handler?.(args)</c> in TS).
/// </summary>
public sealed record TsCallExpression(
    TsExpression Callee,
    IReadOnlyList<TsExpression> Arguments,
    bool Optional = false
) : TsExpression;
