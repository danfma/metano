using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Reports a LINQ call no chain lowered and that no runtime operator or
/// declarative mapping runs as C# would (#648): a <c>System.Linq</c> method
/// nothing lowers (<c>xs.Join(…)</c>, which would otherwise become a member
/// call on the receiver and reach <c>Array.prototype.join</c>), or a mapped
/// one in the static form whose overload its operator lacks. It lowers the
/// call to the throwing placeholder.
/// <para>
/// Thin adapter over the extractor-owned
/// <see cref="IrExpressionExtractor.RewriteUnsupportedLinqCall"/>, which
/// shares the overload queries with the chain walker.
/// </para>
/// <para>
/// Slot order: LAST. Every rewriter that lowers a call must come before it,
/// so a call one of them lowers is never reported.
/// </para>
/// </summary>
public sealed class UnsupportedLinqCallRewriter(
    Func<InvocationExpressionSyntax, IMethodSymbol?, IrExpression?> rewriteUnsupportedLinqCall
) : IInvocationRewriter
{
    private readonly Func<
        InvocationExpressionSyntax,
        IMethodSymbol?,
        IrExpression?
    > _rewriteUnsupportedLinqCall = rewriteUnsupportedLinqCall;

    public IrExpression? Rewrite(InvocationRewriteContext context) =>
        _rewriteUnsupportedLinqCall(context.Invocation, context.Symbol);
}
