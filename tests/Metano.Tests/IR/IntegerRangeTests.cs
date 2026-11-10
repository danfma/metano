using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Tests.IR;

/// <summary>
/// The value ranges the small-integer lowering reads to emit nothing where a
/// conversion or a reduction cannot change the value (spec 012 FR-074–FR-077).
/// An operator whose bound cannot be stated answers <c>null</c>.
/// </summary>
public class IntegerRangeTests
{
    private static readonly IntegerRange Bytes = IntegerRange.Of(IrNumericKind.Byte);
    private static readonly IntegerRange Shorts = IntegerRange.Of(IrNumericKind.Int16);
    private static readonly IntegerRange UnsignedShorts = IntegerRange.Of(IrNumericKind.UInt16);
    private static readonly IntegerRange Ints = IntegerRange.Of(IrNumericKind.Int32);
    private static readonly IntegerRange UnsignedLongs = IntegerRange.Of(IrNumericKind.UInt64);

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
        var range = IntegerRange.Of(kind);

        await Assert.That(range.Min).IsEqualTo((Int128)min);
        await Assert.That(range.Max).IsEqualTo((Int128)max);
    }

    [Test]
    public async Task Of_ReadsTheWholeUnsignedSixtyFourBitRange()
    {
        await Assert.That(UnsignedLongs.Min).IsEqualTo(Int128.Zero);
        await Assert.That(UnsignedLongs.Max).IsEqualTo((Int128)ulong.MaxValue);
    }

    [Test]
    [Arguments(IrNumericKind.Char)]
    [Arguments(IrNumericKind.Double)]
    [Arguments(IrNumericKind.Decimal)]
    [Arguments(IrNumericKind.BigInteger)]
    public async Task Of_RejectsAKindThatIsNotAnInteger(IrNumericKind kind)
    {
        await Assert.That(() => IntegerRange.Of(kind)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Plus_AddsTheBounds()
    {
        var sum = Bytes.Plus(Shorts);

        await Assert.That(sum.Min).IsEqualTo((Int128)(-32768));
        await Assert.That(sum.Max).IsEqualTo((Int128)33022);
    }

    [Test]
    public async Task Minus_SubtractsTheOppositeBounds()
    {
        var difference = Bytes.Minus(Bytes);

        await Assert.That(difference.Min).IsEqualTo((Int128)(-255));
        await Assert.That(difference.Max).IsEqualTo((Int128)255);
    }

    [Test]
    public async Task Times_TakesTheExtremeProducts()
    {
        var product = Shorts.Times(Shorts);

        await Assert.That(product?.Min).IsEqualTo((Int128)(-1073709056));
        await Assert.That(product?.Max).IsEqualTo((Int128)1073741824);
    }

    /// <summary>
    /// <c>ulong.MaxValue * ulong.MaxValue</c> needs 128 unsigned bits, one
    /// more than the bounds hold: the product is unknown, not a wrapped bound.
    /// </summary>
    [Test]
    public async Task Times_IsUnknownWhenABoundDoesNotFit()
    {
        await Assert.That(UnsignedLongs.Times(UnsignedLongs)).IsNull();
    }

    [Test]
    public async Task Negated_SwapsTheBounds()
    {
        var negated = Shorts.Negated();

        await Assert.That(negated.Min).IsEqualTo((Int128)(-32767));
        await Assert.That(negated.Max).IsEqualTo((Int128)32768);
    }

    [Test]
    public async Task BitwiseAnd_IsBoundedByANonNegativeOperand()
    {
        await Assert.That(UnsignedShorts.BitwiseAnd(Bytes)?.Max).IsEqualTo((Int128)255);
        await Assert.That(Ints.BitwiseAnd(Bytes)?.Max).IsEqualTo((Int128)255);
        await Assert.That(Bytes.BitwiseAnd(Ints)?.Min).IsEqualTo(Int128.Zero);
        await Assert.That(Ints.BitwiseAnd(Shorts)).IsNull();
    }

    [Test]
    public async Task BitwiseUnion_SetsNoBitAboveTheHighestMaximum()
    {
        var zero = IntegerRange.Exactly(0);

        await Assert
            .That(Bytes.BitwiseUnion(IntegerRange.Exactly(0x100))?.Max)
            .IsEqualTo((Int128)511);

        await Assert.That(zero.BitwiseUnion(zero)?.Max).IsEqualTo(Int128.Zero);
        await Assert.That(Bytes.BitwiseUnion(Shorts)).IsNull();
        await Assert.That(Shorts.BitwiseUnion(Bytes)).IsNull();
    }

    [Test]
    public async Task ShiftedLeft_ReadsAnUnmaskedCountOnly()
    {
        await Assert.That(Bytes.ShiftedLeft(8)?.Max).IsEqualTo((Int128)65280);
        await Assert.That(Shorts.ShiftedLeft(1)?.Min).IsEqualTo((Int128)(-65536));
        await Assert.That(Bytes.ShiftedLeft(32)).IsNull();
        await Assert.That(Bytes.ShiftedLeft(-1)).IsNull();
    }

    [Test]
    public async Task Contains_ComparesBothBounds()
    {
        await Assert.That(Ints.Contains(Shorts)).IsTrue();
        await Assert.That(Bytes.Contains(Shorts)).IsFalse();
        await Assert.That(Shorts.Contains(UnsignedShorts)).IsFalse();
    }
}
