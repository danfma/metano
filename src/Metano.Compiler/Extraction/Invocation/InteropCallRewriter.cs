using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Lowers the interop calls of the <c>Metano</c> package (<c>Js.Code</c>,
/// <c>UnsafeCast&lt;T&gt;()</c>, <c>AsDynamic()</c>; spec 011 FR-031 – FR-036) to
/// their IR nodes. Thin adapter over the extractor-owned
/// <see cref="IrExpressionExtractor.RewriteInteropCall"/>, which shares the
/// extractor's recursion and type mapping.
/// <para>
/// Slot order: first, since no other lowering applies to these calls.
/// </para>
/// </summary>
public sealed class InteropCallRewriter(
    Func<InvocationExpressionSyntax, IMethodSymbol?, IrExpression?> rewriteInteropCall
) : IInvocationRewriter
{
    private readonly Func<
        InvocationExpressionSyntax,
        IMethodSymbol?,
        IrExpression?
    > _rewriteInteropCall = rewriteInteropCall;

    public IrExpression? Rewrite(InvocationRewriteContext context) =>
        _rewriteInteropCall(context.Invocation, context.Symbol);
}
