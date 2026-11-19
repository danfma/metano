using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Maps a BCL collection type to its <see cref="IrCollectionKind"/> by symbol
/// identity (spec 011 FR-001, FR-054): the definition's namespace and metadata
/// name, never the merged display-name groupings of
/// <see cref="RoslynTypeQueries.IsDictionaryLike"/>,
/// <see cref="RoslynTypeQueries.IsSetLike"/> and
/// <see cref="RoslynTypeQueries.IsCollectionLike"/>, so an immutable or
/// concurrent collection never becomes a mutable kind. Types outside stage
/// 011c (the read-only, immutable, sorted and concurrent collections) and
/// <c>IReadOnlySet&lt;T&gt;</c>, which joins in the 011c flip, resolve to
/// <c>null</c> and keep their 2.x IR.
/// </summary>
public static class CollectionKindResolver
{
    private const string GenericCollectionsNamespace = "System.Collections.Generic";

    private static readonly IReadOnlyDictionary<string, IrCollectionKind> KindsByMetadataName =
        new Dictionary<string, IrCollectionKind>(StringComparer.Ordinal)
        {
            ["List`1"] = IrCollectionKind.List,
            ["IList`1"] = IrCollectionKind.IList,
            ["ICollection`1"] = IrCollectionKind.ICollection,
            ["IReadOnlyList`1"] = IrCollectionKind.IReadOnlyList,
            ["IReadOnlyCollection`1"] = IrCollectionKind.IReadOnlyCollection,
            ["Dictionary`2"] = IrCollectionKind.Dictionary,
            ["IDictionary`2"] = IrCollectionKind.IDictionary,
            ["IReadOnlyDictionary`2"] = IrCollectionKind.IReadOnlyDictionary,
            ["HashSet`1"] = IrCollectionKind.HashSet,
            ["ISet`1"] = IrCollectionKind.ISet,
            ["Queue`1"] = IrCollectionKind.Queue,
            ["Stack`1"] = IrCollectionKind.Stack,
        };

    /// <summary>
    /// The kind of <paramref name="type"/>, or <c>null</c> when it is not a
    /// stage 011c collection.
    /// </summary>
    public static IrCollectionKind? KindOf(INamedTypeSymbol type) =>
        type.ContainingType is { } container
            ? DictionaryViewKindOf(type, container)
            : TopLevelKindOf(type);

    /// <summary>
    /// The type arguments a <see cref="IrCollectionTypeRef"/> of
    /// <paramref name="kind"/> carries for <paramref name="type"/>: the type's
    /// own, or the viewed element of a dictionary view.
    /// </summary>
    public static IReadOnlyList<ITypeSymbol> TypeArgumentsOf(
        INamedTypeSymbol type,
        IrCollectionKind kind
    ) =>
        kind switch
        {
            IrCollectionKind.KeyCollection => [type.ContainingType!.TypeArguments[0]],
            IrCollectionKind.ValueCollection => [type.ContainingType!.TypeArguments[1]],
            _ => type.TypeArguments,
        };

    /// <summary>
    /// The variance of each type argument of <paramref name="type"/>, as its
    /// definition declares it; a dictionary view's element is invariant.
    /// </summary>
    public static IReadOnlyList<IrVariance> VarianceOf(
        INamedTypeSymbol type,
        IrCollectionKind kind
    ) =>
        kind is IrCollectionKind.KeyCollection or IrCollectionKind.ValueCollection
            ? [IrVariance.Invariant]
            : type
                .OriginalDefinition.TypeParameters.Select(parameter =>
                    parameter.Variance == VarianceKind.Out
                        ? IrVariance.Covariant
                        : IrVariance.Invariant
                )
                .ToList();

    private static IrCollectionKind? TopLevelKindOf(INamedTypeSymbol type) =>
        IsInGenericCollectionsNamespace(type)
        && KindsByMetadataName.TryGetValue(type.MetadataName, out var kind)
            ? kind
            : null;

    private static IrCollectionKind? DictionaryViewKindOf(
        INamedTypeSymbol type,
        INamedTypeSymbol container
    ) =>
        TopLevelKindOf(container) is IrCollectionKind.Dictionary
            ? type.MetadataName switch
            {
                "KeyCollection" => IrCollectionKind.KeyCollection,
                "ValueCollection" => IrCollectionKind.ValueCollection,
                _ => null,
            }
            : null;

    private static bool IsInGenericCollectionsNamespace(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == GenericCollectionsNamespace;
}
