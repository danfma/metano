using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// How two parameter types relate by derivation, for ordering the arms of an
/// overload dispatcher (<see cref="OverloadDispatchOrder"/>).
/// </summary>
internal static class TypeSpecificity
{
    public enum Relation
    {
        /// <summary>Neither type derives from the other, or the relation is unknown.</summary>
        Unrelated,

        /// <summary>The same type (nullability and type arguments aside).</summary>
        Same,

        /// <summary>
        /// The first type derives from, or implements, the second, or is a
        /// better numeric conversion target (<see cref="NumericConversionTarget"/>).
        /// </summary>
        MoreDerived,
    }

    /// <summary>
    /// The relation of <paramref name="candidate"/> to <paramref name="other"/>:
    /// the same type, a type that derives from or implements it, or anything
    /// that derives from <c>object</c>.
    /// </summary>
    public static Relation Compare(IrTypeRef candidate, IrTypeRef other)
    {
        var left = WithoutNullability(candidate);
        var right = WithoutNullability(other);

        if (
            NumericConversionTarget.KindOf(left) is { } leftKind
            && NumericConversionTarget.KindOf(right) is { } rightKind
        )
            return CompareNumeric(leftKind, rightKind);

        if (left == right)
            return Relation.Same;

        if (right.IsObject)
            return left.IsObject ? Relation.Same : Relation.MoreDerived;

        if (left is not IrNamedTypeRef leftNamed || right is not IrNamedTypeRef rightNamed)
            return Relation.Unrelated;

        if (leftNamed.SupertypeKey == rightNamed.SupertypeKey)
            return Relation.Same;

        return leftNamed.Semantics?.Supertypes?.Contains(rightNamed.SupertypeKey) is true
            ? Relation.MoreDerived
            : Relation.Unrelated;
    }

    /// <summary>
    /// Two numeric kinds: the same kind, or a better conversion target than
    /// the other, which a dispatcher tries first like a more derived type.
    /// </summary>
    private static Relation CompareNumeric(IrNumericKind candidate, IrNumericKind other)
    {
        if (candidate == other)
            return Relation.Same;

        return NumericConversionTarget.IsBetter(candidate, other)
            ? Relation.MoreDerived
            : Relation.Unrelated;
    }

    private static IrTypeRef WithoutNullability(IrTypeRef type) =>
        type is IrNullableTypeRef nullable ? WithoutNullability(nullable.Inner) : type;
}
