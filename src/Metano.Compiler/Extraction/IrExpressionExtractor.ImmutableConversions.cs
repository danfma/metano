using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A static conversion of an immutable collection to a mutable collection
/// interface (spec 011 FR-054, contracts/runtime-collections.md §
/// Mutable-interface conversions of immutables): .NET's
/// <c>IList&lt;T&gt;</c> / <c>ICollection&lt;T&gt;</c> / <c>ISet&lt;T&gt;</c> /
/// <c>IDictionary&lt;K, V&gt;</c> view of an <c>ImmutableList&lt;T&gt;</c> throws
/// <c>NotSupportedException</c> from its mutators, which a runtime class whose
/// mutators return a new value cannot reproduce, so a mutation through the
/// interface would be dropped silently. Reported as MS0001 for a target whose
/// collections are runtime classes; the value is kept (its reads are sound).
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>Reports the implicit conversion of <paramref name="expression"/> when it is one.</summary>
    private void ReportImmutableAsMutableInterface(ExpressionSyntax expression)
    {
        var info = _semantic.GetTypeInfo(expression);

        ReportImmutableAsMutableInterface(expression, info.Type, info.ConvertedType);
    }

    /// <summary>
    /// Reports the conversion of <paramref name="expression"/> from
    /// <paramref name="source"/> to <paramref name="target"/> when it views an
    /// immutable collection as a mutable collection interface.
    /// </summary>
    private void ReportImmutableAsMutableInterface(
        ExpressionSyntax expression,
        ITypeSymbol? source,
        ITypeSymbol? target
    )
    {
        if (
            _context.KeepsNativeCollections
            || KindOf(source?.UnderlyingValueType()) is not { } sourceKind
            || !IrCollectionKinds.IsImmutable(sourceKind)
            || KindOf(target) is not { } targetKind
            || !IrCollectionKinds.IsMutableInterface(targetKind)
        )
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{expression}' converts '{source!.ToDisplayString()}' to "
                    + $"'{target!.ToDisplayString()}': the immutable collection does not "
                    + "implement the mutable interface at run time, so a mutation through it "
                    + "would return a new collection instead of throwing NotSupportedException.",
                expression.GetLocation()
            )
        );
    }

    private static IrCollectionKind? KindOf(ITypeSymbol? type) =>
        type is INamedTypeSymbol named ? CollectionKindResolver.KindOf(named) : null;
}
