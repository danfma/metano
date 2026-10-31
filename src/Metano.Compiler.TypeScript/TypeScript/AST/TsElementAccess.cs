namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// Element access expression: <c>obj[index]</c>, or the optional-chaining
/// form <c>obj?.[index]</c> when <paramref name="Optional"/> is <c>true</c>.
/// <paramref name="ContinuesChain"/> has the same meaning as on
/// <see cref="TsPropertyAccess"/>: the step belongs to an enclosing optional
/// chain (<c>a?.b[0]</c>).
/// </summary>
public sealed record TsElementAccess(
    TsExpression Object,
    TsExpression Index,
    bool Optional = false,
    bool ContinuesChain = false
) : TsExpression;
