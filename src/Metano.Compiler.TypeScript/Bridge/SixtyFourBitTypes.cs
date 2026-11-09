using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The one definition of the 64-bit integer types a BigInt-mode dispatcher
/// treats alike: <c>long</c>, <c>ulong</c> and <c>BigInteger</c> (nullable
/// included) all lower to a <c>bigint</c>. Shared by the OQ-6 reorder
/// (<see cref="SixtyFourBitDispatchOrder"/>) and the branded-ambiguity
/// detector (<see cref="BrandedSixtyFourBitOverloads"/>).
/// </summary>
internal static class SixtyFourBitTypes
{
    /// <summary>
    /// The primitive of a plain 64-bit integer type, nullable unwrapped;
    /// <c>null</c> for any other type, a branded wrapper included.
    /// </summary>
    public static IrPrimitive? PlainPrimitiveOf(IrTypeRef type) =>
        type switch
        {
            IrPrimitiveTypeRef { Primitive: var primitive } when IsBigIntValued(primitive) =>
                primitive,
            IrNullableTypeRef nullable => PlainPrimitiveOf(nullable.Inner),
            _ => null,
        };

    /// <summary>Whether a primitive lowers to a <c>bigint</c>.</summary>
    public static bool IsBigIntValued(IrPrimitive primitive) =>
        primitive is IrPrimitive.Int64 or IrPrimitive.UInt64 or IrPrimitive.BigInteger;
}
