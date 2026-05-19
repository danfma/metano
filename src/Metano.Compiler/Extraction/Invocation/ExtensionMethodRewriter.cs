using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Lowers extension-method call sites (classic <c>(this T)</c>
/// reduced form or C# 14 <c>extension(T r) { … }</c> block) into
/// module-level helper calls — <c>receiver.Method(args)</c> becomes
/// <c>method(receiver, args)</c>, with static extension members
/// dispatching as <c>Method(args)</c> (no receiver argument).
/// <para>
/// Thin adapter: the rewriter gates on
/// <see cref="IMethodSymbol"/> and forwards to the extractor-owned
/// <see cref="IrExpressionExtractor.TryRewriteExtensionCall(InvocationExpressionSyntax, IMethodSymbol)"/>
/// via the injected delegate. The resolution helpers
/// (<c>TryResolveExtensionLowering</c>,
/// <c>IsTranspilableExtensionContainer</c>) and the canonical
/// builder (<c>BuildExtensionHelperCall</c>) stay on the extractor
/// because they're also called from the property-access and
/// assignment paths (extension properties with <c>$get</c>/<c>$set</c>
/// companions). Migrating them across the seam would force
/// five-plus call sites to thread a façade that only the invocation
/// rewriter actually needs.
/// </para>
/// <para>
/// Slot order: this rewriter sits AFTER
/// <see cref="InlineMethodExpansionRewriter"/>. An extension method
/// can carry <c>[Inline]</c> — the inline branch wins and β-reduces
/// the body, matching the legacy cascade order.
/// </para>
/// </summary>
public sealed class ExtensionMethodRewriter(
    Func<InvocationExpressionSyntax, IMethodSymbol, IrExpression?> rewriteExtensionCall
) : IInvocationRewriter
{
    private readonly Func<
        InvocationExpressionSyntax,
        IMethodSymbol,
        IrExpression?
    > _rewriteExtensionCall = rewriteExtensionCall;

    public IrExpression? Rewrite(InvocationRewriteContext context)
    {
        if (context.Symbol is not IMethodSymbol extensionCallee)
            return null;
        return _rewriteExtensionCall(context.Invocation, extensionCallee);
    }
}
