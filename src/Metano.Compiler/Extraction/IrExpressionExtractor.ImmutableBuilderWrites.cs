using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A write to <c>ImmutableArray&lt;T&gt;.Builder.Count</c> (spec 011 FR-054).
/// .NET's setter truncates the builder or extends it with <c>default(T)</c>,
/// and the runtime builder cannot produce a <c>default(T)</c> it does not know
/// the type of, so the write is reported instead of lowered to a property
/// write on a read-only runtime property.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Reports MS0001 for a write to <c>ImmutableArray&lt;T&gt;.Builder.Count</c>
    /// (<c>b.Count = 1</c>, <c>b.Count += 1</c>, <c>b.Count++</c>) and returns the
    /// placeholder; <c>null</c> when <paramref name="target"/> is not that property.
    /// </summary>
    private IrExpression? TryReportImmutableArrayBuilderCountWrite(
        SyntaxNode write,
        ExpressionSyntax target
    )
    {
        if (
            _context.KeepsNativeCollections
            || _semantic.GetSymbolInfo(target).Symbol
                is not IPropertySymbol { Name: "Count", ContainingType: { } owner }
            || KindOf(owner) is not IrCollectionKind.ImmutableArrayBuilder
        )
            return null;

        return ReportUnsupported(
            write,
            "immutable array builder count write",
            $"Expression '{write}' sets ImmutableArray<T>.Builder.Count, which truncates the "
                + "builder or extends it with default(T) values; the runtime builder cannot "
                + "produce default(T), so the write is not supported by the transpiler. Use "
                + "RemoveRange / Add instead."
        );
    }
}
