using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Which <c>ArgumentOutOfRangeException.ThrowIf*</c> guards the runtime's
/// <c>argument-guards</c> functions lower for a given <c>T</c> (#651). .NET
/// constrains the sign guards to <c>INumberBase&lt;T&gt;</c>, the equality
/// guards to <c>IEquatable&lt;T&gt;</c> and the ordered ones to
/// <c>IComparable&lt;T&gt;</c>; the runtime reproduces those comparisons for
/// the values generated code holds of the kinds below, and nothing else. A
/// guard this policy does not admit keeps no mapping, so the call reports
/// <c>MS0001</c> as an unmapped static member: an ordered comparison of a
/// <c>string</c> is culture-sensitive on .NET, a <c>Guid</c> orders by its
/// bytes and a user type through its own <c>CompareTo</c>; an equality test of
/// a class compares references on .NET, which the runtime's value equality
/// cannot tell from a structural match, and a <c>DateTimeOffset</c> compares
/// instants where its Temporal value compares the time zone too.
/// TODO(#657): a type-argument filter on <c>[MapMethod]</c> would let the
/// declarative mapping, and the mapping port, carry this policy.
/// </summary>
internal static class IrToTsArgumentGuards
{
    private const string GuardsType = "System.ArgumentOutOfRangeException";

    private static readonly HashSet<string> SignGuards =
    [
        "ThrowIfZero",
        "ThrowIfNegative",
        "ThrowIfNegativeOrZero",
    ];

    private static readonly HashSet<string> OrderedGuards =
    [
        "ThrowIfGreaterThan",
        "ThrowIfGreaterThanOrEqual",
        "ThrowIfLessThan",
        "ThrowIfLessThanOrEqual",
    ];

    private static readonly HashSet<IrPrimitive> Numbers =
    [
        IrPrimitive.Byte,
        IrPrimitive.Int16,
        IrPrimitive.Int32,
        IrPrimitive.Int64,
        IrPrimitive.UInt64,
        IrPrimitive.Float32,
        IrPrimitive.Float64,
        IrPrimitive.Decimal,
        IrPrimitive.BigInteger,
        IrPrimitive.Char,
    ];

    private static readonly HashSet<IrPrimitive> OrderedPrimitives =
    [
        .. Numbers,
        IrPrimitive.Boolean,
        IrPrimitive.DateTime,
        IrPrimitive.DateTimeOffset,
        IrPrimitive.DateOnly,
        IrPrimitive.TimeOnly,
        IrPrimitive.TimeSpan,
    ];

    private static readonly HashSet<string> EqualityGuards = ["ThrowIfEqual", "ThrowIfNotEqual"];

    /// <summary>
    /// The primitives whose runtime values <c>valueEquals</c> compares as
    /// <c>EqualityComparer&lt;T&gt;.Default</c> does.
    /// </summary>
    private static readonly HashSet<IrPrimitive> EquatablePrimitives =
    [
        .. Numbers,
        IrPrimitive.Boolean,
        IrPrimitive.String,
        IrPrimitive.Guid,
        IrPrimitive.DateTime,
        IrPrimitive.DateOnly,
        IrPrimitive.TimeOnly,
        IrPrimitive.TimeSpan,
    ];

    /// <summary>
    /// Whether the declarative mapping of <paramref name="call"/> may apply:
    /// every call but a guard over a <c>T</c> the runtime cannot compare as
    /// .NET does.
    /// </summary>
    public static bool Admits(IrCallExpression call)
    {
        if (call.Origin is not { DeclaringTypeFullName: GuardsType, MemberName: var name })
            return true;

        var compared = call.TypeArguments is [var only] ? only : null;

        if (SignGuards.Contains(name))
            return IsNumber(compared);

        if (OrderedGuards.Contains(name))
            return IsOrdered(compared);

        if (EqualityGuards.Contains(name))
            return IsEquatable(compared);

        return true;
    }

    private static bool IsNumber(IrTypeRef? type) =>
        type is IrPrimitiveTypeRef { Primitive: var primitive } && Numbers.Contains(primitive);

    private static bool IsOrdered(IrTypeRef? type) =>
        type is IrPrimitiveTypeRef { Primitive: var primitive }
        && OrderedPrimitives.Contains(primitive);

    /// <summary>
    /// Whether <c>valueEquals</c> answers <c>EqualityComparer&lt;T&gt;.Default</c>
    /// for every value of <paramref name="type"/>: an equatable primitive, an
    /// enum, a branded value, a transpiled record or struct whose fields end,
    /// all the way down, in equatable primitives (no class, array, collection,
    /// <c>object</c> or open <c>T</c> field; a constructed generic record reads
    /// its type arguments), a tuple of those, or a
    /// nullable one.
    /// </summary>
    private static bool IsEquatable(IrTypeRef? type) =>
        type switch
        {
            IrNullableTypeRef nullable => IsEquatable(nullable.Inner),
            IrPrimitiveTypeRef { Primitive: var primitive } => EquatablePrimitives.Contains(
                primitive
            ),
            IrTupleTypeRef tuple => tuple.Elements.All(IsEquatable),
            IrNamedTypeRef { Semantics: { } semantics } => IsEquatableNamed(semantics),
            _ => false,
        };

    private static bool IsEquatableNamed(IrNamedTypeSemantics semantics) =>
        semantics.Kind switch
        {
            IrNamedTypeKind.NumericEnum or IrNamedTypeKind.StringEnum or IrNamedTypeKind.Branded =>
                true,
            IrNamedTypeKind.Record or IrNamedTypeKind.Struct => !semantics.IsExternal
                && semantics.StructuralEqualityLeaves is { } leaves
                && leaves.All(EquatablePrimitives.Contains),
            _ => false,
        };
}
