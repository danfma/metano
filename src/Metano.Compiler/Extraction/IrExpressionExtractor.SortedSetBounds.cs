using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>SortedSet&lt;T&gt;.Min</c> / <c>Max</c> (spec 011 FR-054, D30): C# reads
/// <c>default(T)</c> from an empty set, which is <c>null</c> for reference and
/// <c>Nullable&lt;T&gt;</c> elements only, so the read becomes a call that
/// passes that value explicitly, as <c>List&lt;T&gt;.Find</c> does
/// (IntrinsicBclLoweringTable). A target that keeps native collections keeps
/// the property read.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <c>set.Min</c> → <c>set.Min(default(T))</c> (likewise <c>Max</c>) on a
    /// <c>SortedSet&lt;T&gt;</c>; <c>null</c> for any other member access.
    /// </summary>
    private IrCallExpression? TryExtractSortedSetBound(
        QualifiedMemberSyntax member,
        ISymbol? symbol
    )
    {
        if (
            _context.KeepsNativeCollections
            || symbol is not IPropertySymbol { Name: "Min" or "Max", IsStatic: false } bound
            || CollectionKindResolver.KindOf(bound.ContainingType) is not IrCollectionKind.SortedSet
        )
            return null;

        var elementType = bound.ContainingType.TypeArguments[0];

        return new IrCallExpression(
            new IrMemberAccess(Extract(member.Receiver), bound.Name),
            [new IrArgument(DefaultOrMarker(elementType))],
            Origin: BuildOrigin(bound)
        );
    }
}
