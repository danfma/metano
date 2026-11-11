using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <c>System.MemoryExtensions</c> calls no declarative mapping of the
/// target lowers (#657). C# 14 converts an array (and a string) to a span
/// implicitly, so <c>array.SequenceEqual(other)</c>, <c>array.Sort()</c> or
/// <c>array.Contains(x, comparer)</c> bind to the span extensions. Left to
/// the plain call lowering, each would become a member call on the receiver
/// (<c>array.sequenceEqual(…)</c>) that a JavaScript array does not have.
/// Each reports MS0001 and lowers to the throwing placeholder, as an
/// unlowered LINQ call does.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string MemoryExtensionsTypeName = "System.MemoryExtensions";

    private const string UnloweredSpanCallKind = "UnloweredSpanCall";

    /// <summary>
    /// An invocation rewriter after every lowering one
    /// (<see cref="Invocation.UnsupportedSpanCallRewriter"/>): a
    /// <c>MemoryExtensions</c> call no declarative mapping of the target
    /// lowers reports and becomes the placeholder; <c>null</c> for any other
    /// call.
    /// </summary>
    internal IrExpression? RewriteUnloweredSpanCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? symbol
    )
    {
        if (!IsUnloweredSpanCall(call, symbol))
            return null;

        ReportUnsupportedFeature(
            MethodNameLocationOf(call),
            $"The span method 'MemoryExtensions.{symbol.Name}', which C# binds for an array, "
                + "span or string receiver, has no counterpart for this overload; the call "
                + "throws at run time."
        );

        return new IrUnsupportedExpression(UnloweredSpanCallKind);
    }

    private bool IsUnloweredSpanCall(
        InvocationExpressionSyntax call,
        [NotNullWhen(true)] IMethodSymbol? symbol
    ) =>
        symbol?.ContainingType.ToDisplayString() == MemoryExtensionsTypeName
        && !LowersByTargetMapping(call, symbol);
}
