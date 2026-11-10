using Metano.Compiler.IR;

namespace Metano.Tests.IR;

/// <summary>
/// The kind facts the integers of 32 bits or fewer are lowered by (spec 012
/// FR-074): which kind holds every value of another, and which conversions
/// are narrowings.
/// </summary>
public class NarrowIntegerKindTests
{
    /// <summary>
    /// Rows: the target; columns: whether it holds every value of sbyte,
    /// byte, short, ushort, int and uint.
    /// </summary>
    [Test]
    [Arguments(IrNumericKind.SByte, true, false, false, false, false, false)]
    [Arguments(IrNumericKind.Byte, false, true, false, false, false, false)]
    [Arguments(IrNumericKind.Int16, true, true, true, false, false, false)]
    [Arguments(IrNumericKind.UInt16, false, true, false, true, false, false)]
    [Arguments(IrNumericKind.Int32, true, true, true, true, true, false)]
    [Arguments(IrNumericKind.UInt32, false, true, false, true, false, true)]
    public async Task HoldsEvery_FollowsSignAndWidth(
        IrNumericKind target,
        bool holdsSByte,
        bool holdsByte,
        bool holdsInt16,
        bool holdsUInt16,
        bool holdsInt32,
        bool holdsUInt32
    )
    {
        await Assert.That(target.HoldsEvery(IrNumericKind.SByte)).IsEqualTo(holdsSByte);
        await Assert.That(target.HoldsEvery(IrNumericKind.Byte)).IsEqualTo(holdsByte);
        await Assert.That(target.HoldsEvery(IrNumericKind.Int16)).IsEqualTo(holdsInt16);
        await Assert.That(target.HoldsEvery(IrNumericKind.UInt16)).IsEqualTo(holdsUInt16);
        await Assert.That(target.HoldsEvery(IrNumericKind.Int32)).IsEqualTo(holdsInt32);
        await Assert.That(target.HoldsEvery(IrNumericKind.UInt32)).IsEqualTo(holdsUInt32);
    }

    [Test]
    public async Task IsIntegerNarrowing_LeavesEnumsCharAndWideIntegersAlone()
    {
        var int32 = new IrNumericEndpoint(IrNumericKind.Int32);
        var byteKind = new IrNumericEndpoint(IrNumericKind.Byte);

        await Assert.That(IrNumericConversion.IsIntegerNarrowing(int32, byteKind)).IsTrue();
        await Assert.That(IrNumericConversion.IsIntegerNarrowing(byteKind, int32)).IsFalse();
        await Assert.That(IrNumericConversion.IsIntegerNarrowing(int32, int32)).IsFalse();

        await Assert
            .That(
                IrNumericConversion.IsIntegerNarrowing(
                    new IrNumericEndpoint(IrNumericKind.Int32, IsEnum: true),
                    byteKind
                )
            )
            .IsFalse();

        await Assert
            .That(
                IrNumericConversion.IsIntegerNarrowing(
                    int32,
                    new IrNumericEndpoint(IrNumericKind.Char)
                )
            )
            .IsFalse();

        await Assert
            .That(
                IrNumericConversion.IsIntegerNarrowing(
                    new IrNumericEndpoint(IrNumericKind.Int64),
                    int32
                )
            )
            .IsFalse();
    }

    [Test]
    [Arguments(IrNumericKind.SByte, false, true)]
    [Arguments(IrNumericKind.Byte, false, true)]
    [Arguments(IrNumericKind.Int16, false, true)]
    [Arguments(IrNumericKind.UInt16, false, true)]
    [Arguments(IrNumericKind.Int32, true, false)]
    [Arguments(IrNumericKind.UInt32, true, false)]
    [Arguments(IrNumericKind.Int64, false, false)]
    [Arguments(IrNumericKind.UInt64, false, false)]
    [Arguments(IrNumericKind.Char, false, false)]
    [Arguments(IrNumericKind.Double, false, false)]
    public async Task WidthPredicates_SplitTheKindsAtInt32(
        IrNumericKind kind,
        bool isThirtyTwoBit,
        bool isNarrowerThanInt32
    )
    {
        await Assert.That(kind.IsThirtyTwoBit()).IsEqualTo(isThirtyTwoBit);
        await Assert.That(kind.IsNarrowerThanInt32()).IsEqualTo(isNarrowerThanInt32);

        await Assert
            .That(kind.IsThirtyTwoBitOrNarrower())
            .IsEqualTo(isThirtyTwoBit || isNarrowerThanInt32);
    }
}
