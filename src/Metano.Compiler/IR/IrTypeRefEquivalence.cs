namespace Metano.Compiler.IR;

/// <summary>
/// Structural equality of <see cref="IrTypeRef"/>s. Record equality compares a
/// list member (type arguments, tuple elements) by reference, so two refs that
/// spell <c>List&lt;int&gt;</c> from separate extractions are not equal; this
/// compares the lists element by element.
/// </summary>
public static class IrTypeRefEquivalence
{
    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> name the same type.</summary>
    public static bool AreEquivalent(IrTypeRef? left, IrTypeRef? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (IrNamedTypeRef l, IrNamedTypeRef r) => l.Name == r.Name
                && l.Namespace == r.Namespace
                && AreEquivalent(l.TypeArguments ?? [], r.TypeArguments ?? []),
            (IrNullableTypeRef l, IrNullableTypeRef r) => AreEquivalent(l.Inner, r.Inner),
            (IrArrayTypeRef l, IrArrayTypeRef r) => AreEquivalent(l.ElementType, r.ElementType),
            (IrSetTypeRef l, IrSetTypeRef r) => AreEquivalent(l.ElementType, r.ElementType),
            (IrMapTypeRef l, IrMapTypeRef r) => AreEquivalent(l.KeyType, r.KeyType)
                && AreEquivalent(l.ValueType, r.ValueType),
            (IrTupleTypeRef l, IrTupleTypeRef r) => AreEquivalent(l.Elements, r.Elements),
            (IrPromiseTypeRef l, IrPromiseTypeRef r) => l.IsValueType == r.IsValueType
                && AreEquivalent(l.ResultType, r.ResultType),
            (IrIterableTypeRef l, IrIterableTypeRef r) => AreEquivalent(
                l.ElementType,
                r.ElementType
            ),
            _ => left.Equals(right),
        };

    /// <summary>Whether two type lists are equivalent element by element.</summary>
    public static bool AreEquivalent(
        IReadOnlyList<IrTypeRef> left,
        IReadOnlyList<IrTypeRef> right
    ) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => AreEquivalent(pair.First, pair.Second));
}
