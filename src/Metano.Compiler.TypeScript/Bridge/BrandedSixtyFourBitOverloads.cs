using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Finds the overload sets a BigInt-mode dispatcher cannot tell apart because
/// a <c>[Branded]</c> 64-bit type sits beside another 64-bit type: the
/// branded arm is a <c>typeof === "bigint"</c> test, which accepts every
/// <c>bigint</c> the <c>long</c> / <c>ulong</c> / <c>BigInteger</c> guards
/// accept, so a call that C# binds to one of them can reach another. A set is
/// a group of same-arity overloads whose parameter types are identical except
/// at positions where each side is a <c>bigint</c>-valued type (a 64-bit
/// integer or a branded wrapper over one, nullable included) and which holds
/// a branded member. Groups without a branded member are the documented OQ-6
/// order (<see cref="SixtyFourBitDispatchOrder"/>), and a branded type over a
/// non-64-bit primitive checks a different <c>typeof</c>, so neither is
/// reported. The emitter reports each group as <c>MS0063</c>.
/// </summary>
internal static class BrandedSixtyFourBitOverloads
{
    /// <summary>
    /// Each ambiguous group, members in declaration order; empty when there is
    /// none or the 64-bit integers are not <c>bigint</c> values.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TOverload>> Find<TOverload>(
        IEnumerable<TOverload> overloads,
        Func<TOverload, IReadOnlyList<IrParameter>> parametersOf
    )
    {
        if (!Int64RepresentationScope.IsBigInt)
            return [];

        return overloads
            .Where(overload => parametersOf(overload).Any(p => IsBigIntValued(p.Type)))
            .GroupBy(overload => SignatureOf(parametersOf(overload)))
            .Select(group => group.ToList())
            .Where(group =>
                group.Count > 1 && group.Any(overload => HasBrandedPosition(parametersOf(overload)))
            )
            .Cast<IReadOnlyList<TOverload>>()
            .ToList();
    }

    /// <summary>
    /// The parameter types with every <c>bigint</c>-valued position erased
    /// (a shared marker), so two overloads are in one group exactly when their
    /// signatures are equal.
    /// </summary>
    private static string SignatureOf(IReadOnlyList<IrParameter> parameters) =>
        string.Join(
            "|",
            parameters.Select(p =>
                IsBigIntValued(p.Type) ? "bigint" : IrTypeRefSignatures.Describe(p.Type)
            )
        );

    private static bool HasBrandedPosition(IReadOnlyList<IrParameter> parameters) =>
        parameters.Any(p => BrandedOf(p.Type) is not null);

    private static bool IsBigIntValued(IrTypeRef type) =>
        BrandedOf(type) is not null || PlainSixtyFourBitOf(type) is not null;

    private static IrPrimitive? PlainSixtyFourBitOf(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef { Primitive: var primitive } when IsSixtyFourBit(primitive) =>
                primitive,
            IrNullableTypeRef nullable => PlainSixtyFourBitOf(nullable.Inner),
            _ => null,
        };

    /// <summary>The underlying 64-bit primitive of a branded wrapper, or <c>null</c>.</summary>
    private static IrPrimitive? BrandedOf(IrTypeRef type) =>
        type switch
        {
            IrNamedTypeRef
            {
                Semantics:
                { Kind: IrNamedTypeKind.Branded, BrandedUnderlyingPrimitive: { } primitive },
            } when IsSixtyFourBit(primitive) => primitive,
            IrNullableTypeRef nullable => BrandedOf(nullable.Inner),
            _ => null,
        };

    private static bool IsSixtyFourBit(IrPrimitive primitive) =>
        primitive is IrPrimitive.Int64 or IrPrimitive.UInt64 or IrPrimitive.BigInteger;
}
