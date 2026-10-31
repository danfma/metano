using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// One value an immediately invoked arrow binds to a parameter.
/// </summary>
internal sealed record TsImmediateBinding(string Name, TsExpression Value);

/// <summary>
/// The single self-invoking-arrow builder: binds values to parameters so an
/// expression can reference each more than once while it is evaluated once
/// (let-bindings, null-conditional guards, switch expressions).
/// </summary>
internal static class TsImmediateInvocation
{
    /// <summary>
    /// <c>((name1, name2) =&gt; { body })(value1, value2)</c>. The values are
    /// evaluated left to right before the body runs. The parameters carry no
    /// type annotation: TypeScript types an IIFE parameter from its argument,
    /// which stays exact under every strict flag. A body that awaits
    /// (<paramref name="isAsync"/>, from <c>IrEvaluationTraits.ContainsAwait</c>
    /// on the IR it was lowered from) makes the arrow <c>async</c> and the
    /// call awaited, so the <c>await</c> stays inside an async function.
    /// </summary>
    public static TsExpression Of(
        IReadOnlyList<TsImmediateBinding> bindings,
        IReadOnlyList<TsStatement> body,
        bool isAsync
    )
    {
        var arrow = new TsArrowFunction(
            [.. bindings.Select(binding => new TsParameter(binding.Name, null))],
            body,
            isAsync
        );

        var call = new TsCallExpression(arrow, [.. bindings.Select(binding => binding.Value)]);

        return isAsync ? new TsAwaitExpression(call) : call;
    }
}
