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
/// concurrent collection never becomes a mutable kind. The nested types
/// resolve through their container: the dictionary views of
/// <c>Dictionary</c> and <c>SortedDictionary</c> (one runtime class each) and
/// the <c>Builder</c> of each immutable collection.
/// </summary>
public static class CollectionKindResolver
{
    private static readonly IReadOnlyDictionary<string, IrCollectionKind> KindsByName =
        new Dictionary<string, IrCollectionKind>(StringComparer.Ordinal)
        {
            ["System.Collections.Generic.List`1"] = IrCollectionKind.List,
            ["System.Collections.Generic.IList`1"] = IrCollectionKind.IList,
            ["System.Collections.Generic.ICollection`1"] = IrCollectionKind.ICollection,
            ["System.Collections.Generic.IReadOnlyList`1"] = IrCollectionKind.IReadOnlyList,
            ["System.Collections.Generic.IReadOnlyCollection`1"] =
                IrCollectionKind.IReadOnlyCollection,
            ["System.Collections.Generic.Dictionary`2"] = IrCollectionKind.Dictionary,
            ["System.Collections.Generic.IDictionary`2"] = IrCollectionKind.IDictionary,
            ["System.Collections.Generic.IReadOnlyDictionary`2"] =
                IrCollectionKind.IReadOnlyDictionary,
            ["System.Collections.Generic.HashSet`1"] = IrCollectionKind.HashSet,
            ["System.Collections.Generic.ISet`1"] = IrCollectionKind.ISet,
            ["System.Collections.Generic.IReadOnlySet`1"] = IrCollectionKind.IReadOnlySet,
            ["System.Collections.Generic.Queue`1"] = IrCollectionKind.Queue,
            ["System.Collections.Generic.Stack`1"] = IrCollectionKind.Stack,
            ["System.Collections.Generic.SortedSet`1"] = IrCollectionKind.SortedSet,
            ["System.Collections.Generic.SortedDictionary`2"] = IrCollectionKind.SortedDictionary,
            ["System.Collections.Generic.SortedList`2"] = IrCollectionKind.SortedList,
            ["System.Collections.ObjectModel.ReadOnlyCollection`1"] =
                IrCollectionKind.ReadOnlyCollection,
            ["System.Collections.Concurrent.ConcurrentDictionary`2"] =
                IrCollectionKind.ConcurrentDictionary,
            ["System.Collections.Immutable.ImmutableList`1"] = IrCollectionKind.ImmutableList,
            ["System.Collections.Immutable.ImmutableArray`1"] = IrCollectionKind.ImmutableArray,
            ["System.Collections.Immutable.ImmutableHashSet`1"] = IrCollectionKind.ImmutableHashSet,
            ["System.Collections.Immutable.ImmutableDictionary`2"] =
                IrCollectionKind.ImmutableDictionary,
            ["System.Collections.Immutable.IImmutableList`1"] = IrCollectionKind.IImmutableList,
            ["System.Collections.Immutable.IImmutableSet`1"] = IrCollectionKind.IImmutableSet,
            ["System.Collections.Immutable.IImmutableDictionary`2"] =
                IrCollectionKind.IImmutableDictionary,
        };

    /// <summary>The kind of each immutable collection's nested <c>Builder</c>.</summary>
    private static readonly IReadOnlyDictionary<IrCollectionKind, IrCollectionKind> BuilderKinds =
        new Dictionary<IrCollectionKind, IrCollectionKind>
        {
            [IrCollectionKind.ImmutableList] = IrCollectionKind.ImmutableListBuilder,
            [IrCollectionKind.ImmutableArray] = IrCollectionKind.ImmutableArrayBuilder,
            [IrCollectionKind.ImmutableHashSet] = IrCollectionKind.ImmutableHashSetBuilder,
            [IrCollectionKind.ImmutableDictionary] = IrCollectionKind.ImmutableDictionaryBuilder,
        };

    /// <summary>
    /// The kinds a target that keeps native collections maps
    /// (<see cref="NativeTargetKindOf"/>).
    /// </summary>
    private static readonly IReadOnlySet<IrCollectionKind> NativeTargetKinds =
        new HashSet<IrCollectionKind>
        {
            IrCollectionKind.List,
            IrCollectionKind.IList,
            IrCollectionKind.ICollection,
            IrCollectionKind.IReadOnlyList,
            IrCollectionKind.IReadOnlyCollection,
            IrCollectionKind.Dictionary,
            IrCollectionKind.IDictionary,
            IrCollectionKind.IReadOnlyDictionary,
            IrCollectionKind.HashSet,
            IrCollectionKind.ISet,
            IrCollectionKind.Queue,
            IrCollectionKind.Stack,
            IrCollectionKind.KeyCollection,
            IrCollectionKind.ValueCollection,
        };

    /// <summary>
    /// The kind of <paramref name="type"/>, or <c>null</c> when it is not a
    /// collection of spec 011 FR-001 / FR-054.
    /// </summary>
    public static IrCollectionKind? KindOf(INamedTypeSymbol type) =>
        type.ContainingType is { } container ? NestedKindOf(type, container) : TopLevelKindOf(type);

    /// <summary>
    /// The kind of <paramref name="type"/> for a target that keeps native
    /// collections (Dart before FR-038, spec 011 D28): the stage 011c kinds
    /// but <c>IReadOnlySet&lt;T&gt;</c>, so every other collection keeps its
    /// 2.x IR there; <c>null</c> for any other type.
    /// </summary>
    public static IrCollectionKind? NativeTargetKindOf(INamedTypeSymbol type) =>
        KindOf(type) is { } kind
        && NativeTargetKinds.Contains(kind)
        && (
            type.ContainingType is null
            || TopLevelKindOf(type.ContainingType) is IrCollectionKind.Dictionary
        )
            ? kind
            : null;

    /// <summary>
    /// The type arguments a <see cref="IrCollectionTypeRef"/> of
    /// <paramref name="kind"/> carries for <paramref name="type"/>: the type's
    /// own, the viewed element of a dictionary view, or the container's for a
    /// builder.
    /// </summary>
    public static IReadOnlyList<ITypeSymbol> TypeArgumentsOf(
        INamedTypeSymbol type,
        IrCollectionKind kind
    ) =>
        kind switch
        {
            IrCollectionKind.KeyCollection => [type.ContainingType!.TypeArguments[0]],
            IrCollectionKind.ValueCollection => [type.ContainingType!.TypeArguments[1]],
            _ when IsBuilder(kind) => type.ContainingType!.TypeArguments,
            _ => type.TypeArguments,
        };

    /// <summary>
    /// The variance of each type argument of <paramref name="type"/>, as its
    /// definition declares it; a dictionary view's element and a builder's
    /// arguments are invariant.
    /// </summary>
    public static IReadOnlyList<IrVariance> VarianceOf(
        INamedTypeSymbol type,
        IrCollectionKind kind
    ) =>
        kind switch
        {
            IrCollectionKind.KeyCollection or IrCollectionKind.ValueCollection =>
            [
                IrVariance.Invariant,
            ],
            _ when IsBuilder(kind) =>
            [
                .. type.ContainingType!.TypeArguments.Select(_ => IrVariance.Invariant),
            ],
            _ =>
            [
                .. type.OriginalDefinition.TypeParameters.Select(parameter =>
                    parameter.Variance == VarianceKind.Out
                        ? IrVariance.Covariant
                        : IrVariance.Invariant
                ),
            ],
        };

    private static bool IsBuilder(IrCollectionKind kind) => BuilderKinds.Values.Contains(kind);

    private static IrCollectionKind? TopLevelKindOf(INamedTypeSymbol type) =>
        type.ContainingNamespace is { } ns
        && KindsByName.TryGetValue($"{ns.ToDisplayString()}.{type.MetadataName}", out var kind)
            ? kind
            : null;

    private static IrCollectionKind? NestedKindOf(
        INamedTypeSymbol type,
        INamedTypeSymbol container
    ) =>
        (TopLevelKindOf(container), type.MetadataName) switch
        {
            (IrCollectionKind.Dictionary or IrCollectionKind.SortedDictionary, "KeyCollection") =>
                IrCollectionKind.KeyCollection,
            (IrCollectionKind.Dictionary or IrCollectionKind.SortedDictionary, "ValueCollection") =>
                IrCollectionKind.ValueCollection,
            ({ } containerKind, "Builder")
                when BuilderKinds.TryGetValue(containerKind, out var builder) => builder,
            _ => null,
        };
}
