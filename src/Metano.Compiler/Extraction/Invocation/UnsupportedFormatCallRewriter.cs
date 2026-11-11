using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Governs <c>ToString(format)</c> and <c>ToString(format, provider)</c> of
/// every BCL type (baseline FR-051, #323): a format the target's formatter,
/// the type's declarative mapping or a default-text fact does not cover
/// reports MS0001 and lowers to the throwing placeholder, instead of being
/// emitted as written (a JavaScript method that reads the format as a radix,
/// or ignores it).
/// <para>
/// Thin adapter over the extractor-owned
/// <see cref="IrExpressionExtractor.RewriteUnsupportedFormatCall"/>, which
/// shares the format policy with the interpolation holes.
/// </para>
/// <para>
/// Slot order: before <see cref="UnsupportedLinqCallRewriter"/>; no rewriter
/// above it lowers a BCL <c>ToString(format)</c>.
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
