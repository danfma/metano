namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// <c>new Callee&lt;TypeArguments&gt;(Arguments)</c>; the type arguments are
/// printed only when present.
/// </summary>
public sealed record TsNewExpression(
    TsExpression Callee,
    IReadOnlyList<TsExpression> Arguments,
    IReadOnlyList<TsType>? TypeArguments = null
) : TsExpression;
