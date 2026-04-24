namespace Metano.TypeScript.AST;

/// <summary>
/// A TypeScript function type: <c>(param: T) =&gt; R</c>. When
/// <paramref name="ThisType"/> is set, the printer emits the TS
/// synthetic <c>this</c> annotation as the first parameter
/// (<c>(this: T, …) =&gt; R</c>) — used for delegates whose first
/// parameter carried <c>[This]</c>. Used for delegate type
/// mappings (<c>Action&lt;T&gt;</c>, <c>Func&lt;T,R&gt;</c>,
/// <c>EventHandler&lt;T&gt;</c>, and custom delegate types).
/// </summary>
public sealed record TsFunctionType(
    IReadOnlyList<TsParameter> Parameters,
    TsType ReturnType,
    TsType? ThisType = null
) : TsType;
