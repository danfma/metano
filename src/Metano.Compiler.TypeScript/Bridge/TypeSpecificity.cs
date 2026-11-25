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

        /// <summary>The first type derives from, or implements, the second.</summary>
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

        if (left == right)
            return Relation.Same;

        if (right.IsObject)
            return left.IsObject ? Relation.Same : Relation.MoreDerived;

        if (left is IrFunctionTypeRef leftFunction && right is IrFunctionTypeRef rightFunction)
            return CompareArities(leftFunction, rightFunction);

        if (left is not IrNamedTypeRef leftNamed || right is not IrNamedTypeRef rightNamed)
            return Relation.Unrelated;

        if (leftNamed.SupertypeKey == rightNamed.SupertypeKey)
            return Relation.Same;

        return leftNamed.Semantics?.Supertypes?.Contains(rightNamed.SupertypeKey) is true
            ? Relation.MoreDerived
            : Relation.Unrelated;
    }

    /// <summary>
    /// A delegate of more parameters ranks as the more derived one, so the
    /// dispatcher tries it first, as C# prefers it for a callback that names
    /// every parameter; the arity tests themselves do not overlap
    /// (<see cref="DelegateArityRange"/>, #778).
    /// </summary>
    private static Relation CompareArities(IrFunctionTypeRef left, IrFunctionTypeRef right) =>
        left.Parameters.Count.CompareTo(right.Parameters.Count) switch
        {
            0 => Relation.Same,
            > 0 => Relation.MoreDerived,
            _ => Relation.Unrelated,
        };

    private static IrTypeRef WithoutNullability(IrTypeRef type) =>
        type is IrNullableTypeRef nullable ? WithoutNullability(nullable.Inner) : type;
}
