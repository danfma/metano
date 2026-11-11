using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Reports a <c>System.MemoryExtensions</c> call that no declarative mapping
/// of the target lowers (#657): C# 14 binds <c>array.SequenceEqual(other)</c>
/// and the other span extensions there, and the plain call lowering would
/// otherwise emit a member call on the receiver that a JavaScript array does
/// not have. It lowers the call to the throwing placeholder.
/// <para>
/// Thin adapter over the extractor-owned
/// <see cref="IrExpressionExtractor.RewriteUnloweredSpanCall"/>, which shares
/// the declarative-mapping port with the LINQ report.
/// </para>
/// <para>
/// Slot order: after every rewriter that lowers a call, so a call one of
/// them lowers is never reported.
/// </para>
/// </summary>
public sealed class UnsupportedSpanCallRewriter(
    Func<InvocationExpressionSyntax, IMethodSymbol?, IrExpression?> rewriteUnloweredSpanCall
) : IInvocationRewriter
{
    private readonly Func<
        InvocationExpressionSyntax,
        IMethodSymbol?,
        IrExpression?
    > _rewriteUnloweredSpanCall = rewriteUnloweredSpanCall;

    public IrExpression? Rewrite(InvocationRewriteContext context) =>
        _rewriteUnloweredSpanCall(context.Invocation, context.Symbol);
}
