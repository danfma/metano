namespace Metano.Compiler.IR;

/// <summary>
/// The neutral shape facts of an <see cref="IrCollectionKind"/>: which kinds
/// address their elements by key, which hold a set of distinct elements, which
/// are immutable and which are the mutable interfaces (spec 011 FR-001,
/// FR-054). The one rule every target and extraction site
/// reads them from.
/// </summary>
public static class IrCollectionKinds
{
    /// <summary>
    /// Whether elements of <paramref name="kind"/> are addressed by key: the
    /// dictionaries, their interfaces and the immutable dictionary builder.
    /// </summary>
    public static bool IsKeyed(IrCollectionKind kind) =>
        kind
            is IrCollectionKind.Dictionary
                or IrCollectionKind.IDictionary
                or IrCollectionKind.IReadOnlyDictionary
                or IrCollectionKind.ImmutableDictionary
                or IrCollectionKind.IImmutableDictionary
                or IrCollectionKind.ImmutableDictionaryBuilder
                or IrCollectionKind.SortedDictionary
                or IrCollectionKind.SortedList
                or IrCollectionKind.ConcurrentDictionary;

    /// <summary>
    /// Whether <paramref name="kind"/> holds a set of distinct elements: the
    /// sets, their interfaces and the immutable set builder.
    /// </summary>
    public static bool IsSet(IrCollectionKind kind) =>
        kind
            is IrCollectionKind.HashSet
                or IrCollectionKind.ISet
                or IrCollectionKind.IReadOnlySet
                or IrCollectionKind.ImmutableHashSet
                or IrCollectionKind.IImmutableSet
                or IrCollectionKind.ImmutableHashSetBuilder
                or IrCollectionKind.SortedSet;

    /// <summary>
    /// Whether <paramref name="kind"/> is an immutable collection whose .NET
    /// type implements the mutable collection interfaces with mutators that
    /// throw <c>NotSupportedException</c>: <c>ImmutableList&lt;T&gt;</c>,
    /// <c>ImmutableArray&lt;T&gt;</c>, <c>ImmutableHashSet&lt;T&gt;</c> and
    /// <c>ImmutableDictionary&lt;K, V&gt;</c>.
    /// </summary>
    public static bool IsImmutable(IrCollectionKind kind) =>
        kind
            is IrCollectionKind.ImmutableList
                or IrCollectionKind.ImmutableArray
                or IrCollectionKind.ImmutableHashSet
                or IrCollectionKind.ImmutableDictionary;

    /// <summary>
    /// Whether <paramref name="kind"/> is a mutable collection interface:
    /// <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>ISet&lt;T&gt;</c> or
    /// <c>IDictionary&lt;K, V&gt;</c>.
    /// </summary>
    public static bool IsMutableInterface(IrCollectionKind kind) =>
        kind
            is IrCollectionKind.IList
                or IrCollectionKind.ICollection
                or IrCollectionKind.ISet
                or IrCollectionKind.IDictionary;
}
