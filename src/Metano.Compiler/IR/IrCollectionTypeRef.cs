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
/// element) and the variance of each argument, read from the BCL definition
/// (carried for the covariant type tests of FR-008, which a later stage
/// renders). Two refs are equal when their kinds are and their type arguments
/// are element by element (spec 011 data-model § 2, the final rule of the
/// flip): <c>List&lt;int&gt;</c> is neither <c>int[]</c> nor
/// <c>IList&lt;int&gt;</c>, since each renders a different TypeScript type.
/// </summary>
public sealed record IrCollectionTypeRef(
    IrCollectionKind Kind,
    IReadOnlyList<IrTypeRef> TypeArguments,
    IReadOnlyList<IrVariance> Variance
) : IrTypeRef
{
    /// <summary>
    /// The pre-011c IR of this type, computed on each read (it allocates one
    /// small ref): <see cref="IrArrayTypeRef"/> for the list-shaped kinds, the
    /// queue, the stack and the dictionary views,
    /// <see cref="IrIterableTypeRef"/> for <c>IReadOnlyCollection&lt;T&gt;</c>,
    /// <see cref="IrMapTypeRef"/> for the dictionaries and
    /// <see cref="IrSetTypeRef"/> for the sets. Transitional (spec 011
    /// data-model § 2, D28): read by the targets that keep native collections
    /// (the Dart type mapper); FR-038 removes it.
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
            IrCollectionKind.HashSet or IrCollectionKind.ISet or IrCollectionKind.IReadOnlySet =>
                new IrSetTypeRef(TypeArguments[0]),
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
/// the code paths of a target that keeps native collections (spec 011
/// data-model § 2).
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

    /// <summary>
    /// The array a type lowers to before the flip: a C# array, or a
    /// list-shaped collection, either one nullable or not; <c>null</c> for any
    /// other type.
    /// </summary>
    public static IrArrayTypeRef? AsLegacyArray(this IrTypeRef? type) =>
        type is IrNullableTypeRef nullable
            ? nullable.Inner.WithLegacyCollection() as IrArrayTypeRef
            : type.WithLegacyCollection() as IrArrayTypeRef;

    /// <summary>
    /// <paramref name="type"/> with every collection ref, at every nesting
    /// level, replaced by its pre-011c form, for the sites that compare two
    /// types by record equality as main did. A ref with no collection inside
    /// is returned as the same instance.
    /// </summary>
    public static IrTypeRef ToLegacyTypeRef(this IrTypeRef type) =>
        type switch
        {
            IrCollectionTypeRef collection => collection.LegacyForm.ToLegacyTypeRef(),
            IrNullableTypeRef nullable => Rebuilt(nullable, nullable.Inner, inner =>
                nullable with
                {
                    Inner = inner,
                }
            ),
            IrArrayTypeRef array => Rebuilt(array, array.ElementType, element =>
                array with
                {
                    ElementType = element,
                }
            ),
            IrSetTypeRef set => Rebuilt(set, set.ElementType, element =>
                set with
                {
                    ElementType = element,
                }
            ),
            IrIterableTypeRef iterable => Rebuilt(iterable, iterable.ElementType, element =>
                iterable with
                {
                    ElementType = element,
                }
            ),
            IrIteratorTypeRef iterator => Rebuilt(iterator, iterator.ElementType, element =>
                iterator with
                {
                    ElementType = element,
                }
            ),
            IrGeneratorTypeRef generator => Rebuilt(generator, generator.YieldType, yield =>
                generator with
                {
                    YieldType = yield,
                }
            ),
            IrPromiseTypeRef promise => Rebuilt(promise, promise.ResultType, result =>
                promise with
                {
                    ResultType = result,
                }
            ),
            IrMapTypeRef map => RebuiltPair(map, map.KeyType, map.ValueType, (key, value) =>
                map with
                {
                    KeyType = key,
                    ValueType = value,
                }
            ),
            IrKeyValuePairTypeRef pair => RebuiltPair(
                pair,
                pair.KeyType,
                pair.ValueType,
                (key, value) => pair with { KeyType = key, ValueType = value }
            ),
            IrGroupingTypeRef grouping => RebuiltPair(
                grouping,
                grouping.KeyType,
                grouping.ElementType,
                (key, element) => grouping with { KeyType = key, ElementType = element }
            ),
            IrTupleTypeRef tuple => RebuiltList(tuple, tuple.Elements, elements =>
                tuple with
                {
                    Elements = elements,
                }
            ),
            IrNamedTypeRef { TypeArguments: { } arguments } named => RebuiltList(
                named,
                arguments,
                rebuilt => named with { TypeArguments = rebuilt }
            ),
            IrFunctionTypeRef function => LegacyFunction(function),
            _ => type,
        };

    private static IrTypeRef LegacyFunction(IrFunctionTypeRef function)
    {
        var parameters = function
            .Parameters.Select(parameter =>
                parameter with
                {
                    Type = parameter.Type.ToLegacyTypeRef(),
                }
            )
            .ToList();

        var returnType = function.ReturnType.ToLegacyTypeRef();
        var thisType = function.ThisType?.ToLegacyTypeRef();

        var unchanged =
            ReferenceEquals(returnType, function.ReturnType)
            && ReferenceEquals(thisType, function.ThisType)
            && parameters
                .Zip(function.Parameters)
                .All(pair => ReferenceEquals(pair.First.Type, pair.Second.Type));

        return unchanged
            ? function
            : function with
            {
                Parameters = parameters,
                ReturnType = returnType,
                ThisType = thisType,
            };
    }

    private static IrTypeRef Rebuilt(
        IrTypeRef type,
        IrTypeRef child,
        Func<IrTypeRef, IrTypeRef> rebuild
    )
    {
        var legacy = child.ToLegacyTypeRef();

        return ReferenceEquals(legacy, child) ? type : rebuild(legacy);
    }

    private static IrTypeRef RebuiltPair(
        IrTypeRef type,
        IrTypeRef first,
        IrTypeRef second,
        Func<IrTypeRef, IrTypeRef, IrTypeRef> rebuild
    )
    {
        var legacyFirst = first.ToLegacyTypeRef();
        var legacySecond = second.ToLegacyTypeRef();

        var unchanged =
            ReferenceEquals(legacyFirst, first) && ReferenceEquals(legacySecond, second);

        return unchanged ? type : rebuild(legacyFirst, legacySecond);
    }

    private static IrTypeRef RebuiltList(
        IrTypeRef type,
        IReadOnlyList<IrTypeRef> children,
        Func<IReadOnlyList<IrTypeRef>, IrTypeRef> rebuild
    )
    {
        var legacy = children.Select(ToLegacyTypeRef).ToList();
        var unchanged = legacy.Zip(children).All(pair => ReferenceEquals(pair.First, pair.Second));

        return unchanged ? type : rebuild(legacy);
    }
}
