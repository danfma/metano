using Metano.Compiler.IR;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Rewrites <c>handler.Invoke(args)</c> into <c>handler(args)</c> —
/// the delegate's synthetic <c>Invoke</c> member has no runtime
/// counterpart in JS/TS because the delegate IS the function. Drops
/// the <c>.Invoke</c> indirection at the source level so consumers
/// read dispatch as a plain function call.
/// <para>
/// <c>[This]</c>-bearing delegates fall through (return
/// <see langword="null"/>) so the downstream
/// <c>delegate.call(receiver, …)</c> rewrite still runs and rebinds
/// <c>this</c> via the runtime trampoline.
/// </para>
/// <para>
/// First rewriter in the chain. Sits ahead of
/// <see cref="IntrinsicBclLoweringTable"/>, <c>[Emit]</c> templates,
/// <c>[Import]</c> facades, and <c>[Inline]</c> expansion so a
/// hypothetical future BCL delegate whose <c>Invoke</c> happens to
/// carry one of those attributes still lowers as a delegate call
/// here. Slot order is pinned by the canary tests in
/// <c>InvocationRewriterCanaryTests</c> — change the array order and
/// the canaries fire loudly instead of silently re-routing the call.
/// </para>
/// </summary>
public sealed class DelegateInvokeRewriter(InvocationLoweringHelpers helpers) : IInvocationRewriter
{
    private readonly InvocationLoweringHelpers _helpers = helpers;

    public IrExpression? Rewrite(InvocationRewriteContext context)
    {
        if (context.Symbol is not { } invoke)
            return null;
        if (!InvocationLoweringHelpers.IsDelegateInvoke(invoke))
            return null;
        if (InvocationLoweringHelpers.HasThisParameter(invoke))
            return null;
        if (context.Invocation.Expression is not MemberAccessExpressionSyntax invokeMember)
            return null;

        var receiver = context.Recurse(invokeMember.Expression);
        var args = context
            .Invocation.ArgumentList.Arguments.Select(context.ExtractArgument)
            .ToList();
        _helpers.ApplyParamsSpread(args, invoke, context.Invocation.ArgumentList.Arguments);
        return new IrCallExpression(receiver, args, Origin: _helpers.BuildOrigin(invoke));
    }
}
