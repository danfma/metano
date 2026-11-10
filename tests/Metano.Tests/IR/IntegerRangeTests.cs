using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Tests.IR;

/// <summary>
/// The value ranges the small-integer lowering reads to emit nothing where a
/// conversion or a reduction cannot change the value (spec 012 FR-074–FR-077).
/// </summary>
public class IntegerRangeTests
{
    [Test]
    [Arguments(IrNumericKind.SByte, -128L, 127L)]
    [Arguments(IrNumericKind.Byte, 0L, 255L)]
    [Arguments(IrNumericKind.Int16, -32768L, 32767L)]
    [Arguments(IrNumericKind.UInt16, 0L, 65535L)]
    [Arguments(IrNumericKind.Int32, -2147483648L, 2147483647L)]
    [Arguments(IrNumericKind.UInt32, 0L, 4294967295L)]
    [Arguments(IrNumericKind.Int64, long.MinValue, long.MaxValue)]
    public async Task Of_IsTheRangeOfTheIntegerKind(IrNumericKind kind, long min, long max)
    {
        var range = IntegerRange.Of(kind).GetValueOrDefault();

        await Assert.That(range.Min).IsEqualTo((Int128)min);
        await Assert.That(range.Max).IsEqualTo((Int128)max);
    }

    [Test]
    public async Task Of_ReadsTheWholeUnsignedSixtyFourBitRange()
    {
        var range = IntegerRange.Of(IrNumericKind.UInt64).GetValueOrDefault();

        await Assert.That(range.Min).IsEqualTo(Int128.Zero);
        await Assert.That(range.Max).IsEqualTo((Int128)ulong.MaxValue);
    }

    [Test]
    [Arguments(IrNumericKind.Char)]
    [Arguments(IrNumericKind.Double)]
    [Arguments(IrNumericKind.Decimal)]
    [Arguments(IrNumericKind.BigInteger)]
    public async Task Of_IsNullForAKindThatIsNotAnInteger(IrNumericKind kind)
    {
        await Assert.That(IntegerRange.Of(kind)).IsNull();
    }

    [Test]
    public async Task Arithmetic_CombinesTheBounds()
    {
        var bytes = IntegerRange.Of(IrNumericKind.Byte).GetValueOrDefault();
        var shorts = IntegerRange.Of(IrNumericKind.Int16).GetValueOrDefault();

        await Assert.That(bytes.Plus(bytes).Max).IsEqualTo((Int128)510);
        await Assert.That(bytes.Minus(bytes).Min).IsEqualTo((Int128)(-255));
        await Assert.That(shorts.Times(shorts).Max).IsEqualTo((Int128)1073741824);
        await Assert.That(shorts.Times(shorts).Min).IsEqualTo((Int128)(-1073709056));
        await Assert.That(shorts.Negated().Max).IsEqualTo((Int128)32768);
    }

    [Test]
    public async Task BitwiseOperators_BoundNonNegativeOperands()
    {
        var bytes = IntegerRange.Of(IrNumericKind.Byte).GetValueOrDefault();
        var unsignedShorts = IntegerRange.Of(IrNumericKind.UInt16).GetValueOrDefault();
        var mask = IntegerRange.Exactly(0x100);
        var zero = IntegerRange.Exactly(0);

        await Assert.That(unsignedShorts.BitwiseAnd(bytes).Max).IsEqualTo((Int128)255);
        await Assert.That(bytes.BitwiseUnion(mask).Max).IsEqualTo((Int128)511);
        await Assert.That(zero.BitwiseUnion(zero).Max).IsEqualTo(Int128.Zero);
    }

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
}
