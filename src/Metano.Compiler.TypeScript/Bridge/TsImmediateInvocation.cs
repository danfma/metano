using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Builds a self-invoking arrow that binds one value to a parameter so an
/// expression can reference it more than once while it is evaluated once.
/// </summary>
internal static class TsImmediateInvocation
{
    /// <summary>
    /// <c>((parameterName) =&gt; { body })(argument)</c>. The parameter
    /// carries no type annotation: TypeScript types an IIFE parameter from
    /// its argument, which stays exact under every strict flag. An awaiting
    /// body makes the arrow <c>async</c> and the call awaited, so the
    /// <c>await</c> stays inside an async function.
    /// </summary>
    public static TsExpression Of(
        string parameterName,
        TsExpression argument,
        IReadOnlyList<TsStatement> body,
        bool isAsync
    )
    {
        var arrow = new TsArrowFunction([new TsParameter(parameterName, null)], body, isAsync);
        var call = new TsCallExpression(arrow, [argument]);

        return isAsync ? new TsAwaitExpression(call) : call;
    }
}
