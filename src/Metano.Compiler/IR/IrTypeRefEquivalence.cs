namespace Metano.Compiler.IR;

/// <summary>
/// Structural equality of <see cref="IrTypeRef"/>s. Record equality compares a
/// list member (type arguments, tuple elements, function parameters, callable
/// signatures) by reference, so two refs that spell <c>List&lt;int&gt;</c> from
/// separate extractions are not equal; this compares every nested ref and list
/// element by element. A node it does not know answers "not equivalent", the
/// conservative answer for a caller that skips a declaration only when two refs
/// are the same.
/// </summary>
public static class IrTypeRefEquivalence
{
    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> name the same type.</summary>
    public static bool AreEquivalent(IrTypeRef? left, IrTypeRef? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (IrPrimitiveTypeRef l, IrPrimitiveTypeRef r) => l.Primitive == r.Primitive,
            (IrTypeParameterRef l, IrTypeParameterRef r) => l.Name == r.Name,
            (IrNamedTypeRef l, IrNamedTypeRef r) => l.Name == r.Name
                && l.Namespace == r.Namespace
                && l.PrivateNestedSegments == r.PrivateNestedSegments
                && AreEquivalent(l.TypeArguments ?? [], r.TypeArguments ?? []),
            (IrNullableTypeRef l, IrNullableTypeRef r) => AreEquivalent(l.Inner, r.Inner),
            // Until the 011c flip a collection compares as its pre-011c form
            // (spec 011 data-model § 2, W4-23): List<int> is equivalent to
            // int[] and to IList<int>, as on main.
            (IrCollectionTypeRef l, _) => AreEquivalent(l.LegacyForm, right),
            (_, IrCollectionTypeRef r) => AreEquivalent(left, r.LegacyForm),
            (IrArrayTypeRef l, IrArrayTypeRef r) => AreEquivalent(l.ElementType, r.ElementType),
            (IrSetTypeRef l, IrSetTypeRef r) => AreEquivalent(l.ElementType, r.ElementType),
            (IrIterableTypeRef l, IrIterableTypeRef r) => AreEquivalent(
                l.ElementType,
                r.ElementType
            ),
            (IrIteratorTypeRef l, IrIteratorTypeRef r) => AreEquivalent(
                l.ElementType,
                r.ElementType
            ),
            (IrGeneratorTypeRef l, IrGeneratorTypeRef r) => AreEquivalent(l.YieldType, r.YieldType),
            (IrPromiseTypeRef l, IrPromiseTypeRef r) => l.IsValueType == r.IsValueType
                && AreEquivalent(l.ResultType, r.ResultType),
            (IrMapTypeRef l, IrMapTypeRef r) => AreEquivalent(l.KeyType, r.KeyType)
                && AreEquivalent(l.ValueType, r.ValueType),
            (IrKeyValuePairTypeRef l, IrKeyValuePairTypeRef r) => AreEquivalent(
                l.KeyType,
                r.KeyType
            ) && AreEquivalent(l.ValueType, r.ValueType),
            (IrGroupingTypeRef l, IrGroupingTypeRef r) => AreEquivalent(l.KeyType, r.KeyType)
                && AreEquivalent(l.ElementType, r.ElementType),
            (IrTupleTypeRef l, IrTupleTypeRef r) => AreEquivalent(l.Elements, r.Elements),
            (IrFunctionTypeRef l, IrFunctionTypeRef r) => AreEquivalent(l, r),
            (IrCallableTypeRef l, IrCallableTypeRef r) => l.Signatures.Count == r.Signatures.Count
                && l.Signatures.Zip(r.Signatures)
                    .All(pair => AreEquivalent(pair.First, pair.Second)),

            // An unknown type, or a node this comparison does not know:
            // conservatively not equivalent.
            _ => false,
        };

    /// <summary>Whether two type lists are equivalent element by element.</summary>
    public static bool AreEquivalent(
        IReadOnlyList<IrTypeRef> left,
        IReadOnlyList<IrTypeRef> right
    ) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => AreEquivalent(pair.First, pair.Second));

    private static bool AreEquivalent(IrFunctionTypeRef left, IrFunctionTypeRef right) =>
        AreEquivalent(left.ReturnType, right.ReturnType)
        && AreEquivalent(left.ThisType, right.ThisType)
        && left.Parameters.Count == right.Parameters.Count
        && left.Parameters.Zip(right.Parameters)
            .All(pair =>
                pair.First.IsParams == pair.Second.IsParams
                && pair.First.CanBeOmitted == pair.Second.CanBeOmitted
                && AreEquivalent(pair.First.Type, pair.Second.Type)
            );
}
