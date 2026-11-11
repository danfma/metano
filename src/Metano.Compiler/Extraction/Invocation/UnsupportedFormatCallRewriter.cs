using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Reports <c>ToString(format)</c> on a 32-bit or narrower integer, a
/// <c>double</c> or a <c>float</c> whose format the target's runtime does not
/// format as .NET does (baseline FR-051, #323) and lowers the call to the
/// throwing placeholder, instead of letting the declarative mapping call the
/// formatter with a format it rejects or a JavaScript method that reads the
/// format as a radix.
/// <para>
/// Thin adapter over the extractor-owned
/// <see cref="IrExpressionExtractor.RewriteUnsupportedFormatCall"/>, which
/// shares the format policy with the interpolation holes.
/// </para>
/// <para>
/// Slot order: before <see cref="UnsupportedLinqCallRewriter"/>; no rewriter
/// above it lowers <c>ToString</c> on a number.
/// </para>
/// </summary>
public sealed class UnsupportedFormatCallRewriter(
    Func<InvocationExpressionSyntax, IMethodSymbol?, IrExpression?> rewriteUnsupportedFormatCall
) : IInvocationRewriter
{
    private readonly Func<
        InvocationExpressionSyntax,
        IMethodSymbol?,
        IrExpression?
    > _rewriteUnsupportedFormatCall = rewriteUnsupportedFormatCall;

    public IrExpression? Rewrite(InvocationRewriteContext context) =>
        _rewriteUnsupportedFormatCall(context.Invocation, context.Symbol);
}
