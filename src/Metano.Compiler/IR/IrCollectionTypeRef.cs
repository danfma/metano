namespace Metano.Compiler.IR;

/// <summary>
/// The BCL collection a <see cref="IrCollectionTypeRef"/> names: one value per
/// runtime class and interface of spec 011 FR-001 (stage 011c's classes,
/// their interfaces and the dictionary views). Stage 011h adds the
/// read-only, immutable, sorted and concurrent kinds.
/// </summary>
public enum IrCollectionKind
{
    List,
    IList,
    ICollection,
    IReadOnlyList,
    IReadOnlyCollection,
    Dictionary,
    IDictionary,
    IReadOnlyDictionary,
    HashSet,
    ISet,
    IReadOnlySet,
    Queue,
    Stack,
    KeyCollection,
    ValueCollection,
}

/// <summary>
/// The variance of a collection type parameter, as its BCL declaration
/// states it (<c>IReadOnlyList&lt;out T&gt;</c> is covariant).
/// </summary>
public enum IrVariance
{
    Invariant,
    Covariant,
}

/// <summary>
/// A BCL collection type (spec 011 FR-001): its kind, its type arguments
/// (the element, or the key and value; a dictionary view carries its own
/// element) and the variance of each argument. Equality compares the kind and
/// the arguments element by element; the variance follows from the kind.
/// </summary>
public sealed record IrCollectionTypeRef(
    IrCollectionKind Kind,
    IReadOnlyList<IrTypeRef> TypeArguments,
    IReadOnlyList<IrVariance> Variance
) : IrTypeRef
{
    /// <summary>
    /// The pre-011c IR of this type: <see cref="IrArrayTypeRef"/> for the
    /// list-shaped kinds, the queue, the stack and the dictionary views,
    /// <see cref="IrIterableTypeRef"/> for <c>IReadOnlyCollection&lt;T&gt;</c>,
    /// <see cref="IrMapTypeRef"/> for the dictionaries and
    /// <see cref="IrSetTypeRef"/> for <c>HashSet&lt;T&gt;</c> and
    /// <c>ISet&lt;T&gt;</c>. Transitional (spec 011 data-model § 2, D28): the
    /// targets whose collections are still native render it; FR-038 removes it.
    /// </summary>
    public IrTypeRef LegacyForm =>
        Kind switch
        {
            IrCollectionKind.IReadOnlyCollection => new IrIterableTypeRef(TypeArguments[0]),
            IrCollectionKind.Dictionary
            or IrCollectionKind.IDictionary
            or IrCollectionKind.IReadOnlyDictionary => new IrMapTypeRef(
                TypeArguments[0],
                TypeArguments[1]
            ),
            IrCollectionKind.HashSet or IrCollectionKind.ISet => new IrSetTypeRef(TypeArguments[0]),
            IrCollectionKind.IReadOnlySet => throw new InvalidOperationException(
                "IReadOnlySet<T> has no pre-011c collection form: it joins the resolver in the flip."
            ),
            _ => new IrArrayTypeRef(TypeArguments[0]),
        };

    public bool Equals(IrCollectionTypeRef? other) =>
        other is not null && Kind == other.Kind && TypeArguments.SequenceEqual(other.TypeArguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);

        foreach (var argument in TypeArguments)
            hash.Add(argument);

        return hash.ToHashCode();
    }
}

/// <summary>
/// Unwraps the transitional <see cref="IrCollectionTypeRef.LegacyForm"/> for
/// the code paths that still render 2.x collections.
/// </summary>
public static class IrCollectionTypeRefLegacy
{
    /// <summary>
    /// <paramref name="type"/> with a top-level collection ref replaced by its
    /// pre-011c form (nested arguments are left as they are: each walker
    /// unwraps a nested ref when it reaches it).
    /// </summary>
    public static IrTypeRef? WithLegacyCollection(this IrTypeRef? type) =>
        type is IrCollectionTypeRef collection ? collection.LegacyForm : type;
}
