using System;
using System.Threading.Tasks;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The shapes around the integers of 32 bits or fewer that the conversion,
/// store and operator matrices do not reach (spec 012 FR-074–FR-077, issue
/// #639): operands whose values the target holds, so the narrowing is
/// omitted, next to ones where it is not; nested casts; static fields,
/// captured variables and a receiver with a side effect; the element
/// conversion of a <c>foreach</c>; tuple casts; nested <c>checked</c> /
/// <c>unchecked</c> contexts; a checked expression inside a string; the
/// range reading of <c>&lt;&lt;</c> by a constant, each next to a row that
/// must reduce; shift counts at and past the 5-bit mask; bitwise operators
/// with a negative operand; and compound shifts through a receiver, an
/// index and a property with side effects, which must run once each.
/// </summary>
[NoContainer]
public static class NarrowShapes
{
    private const int LowByte = 0xFF;

    private static readonly int RuntimeLowByte = 0xFF;

    public static sbyte MaskToSByte(int source) => (sbyte)(source & 0x7F);

    public static sbyte WideMaskToSByte(int source) => (sbyte)(source & 0xFF);

    public static byte SBytePlus128(sbyte a) => (byte)(a + 128);

    public static byte SBytePlus129(sbyte a) => (byte)(a + 129);

    public static ushort BytesProductToUShort(byte a, byte b) => (ushort)(a * b);

    public static byte BytesProductToByte(byte a, byte b) => (byte)(a * b);

    public static int UIntLowBitsToInt(uint source) => (int)(source & 0x7FFFFFFF);

    public static int UIntAllBitsToInt(uint source) => (int)(source & 0xFFFFFFFF);

    public static byte ConstMaskToByte(int source) => (byte)(source & LowByte);

    public static byte ReadonlyMaskToByte(int source) => (byte)(source & RuntimeLowByte);

    public static byte MaskToByteChecked(int source) => checked((byte)(source & LowByte));

    public static byte ReadonlyMaskToByteChecked(int source) =>
        checked((byte)(source & (RuntimeLowByte << 1)));

    public static sbyte IntToByteToSByte(int source) => (sbyte)(byte)source;

    public static uint UIntToShortToUInt(uint source) => (uint)(short)source;

    public static int IntToUShortToInt(int source) => (int)(ushort)source;

    public static byte DoubleToIntToByte(double source) => (byte)(int)source;

    public static byte LongToIntToByte(long source) => (byte)(int)source;

    public static sbyte IntToByteToSByteChecked(int source) => checked((sbyte)(byte)source);

    public static uint IntToShortToUIntMixed(int source) => checked((uint)unchecked((short)source));

    public static byte StaticFieldCompound(byte start, byte amount)
    {
        NarrowStatics.Count = start;
        NarrowStatics.Count += amount;
        NarrowStatics.Count++;

        return NarrowStatics.Count;
    }

    public static short StaticFieldCompoundChecked(short start, short amount)
    {
        NarrowStatics.Level = start;
        checked
        {
            NarrowStatics.Level -= amount;
            --NarrowStatics.Level;
        }

        return NarrowStatics.Level;
    }

    public static byte CapturedIncrement(byte start)
    {
        var counter = start;
        Action step = () => counter++;

        step();
        step();

        return counter;
    }

    public static sbyte CapturedCompoundChecked(sbyte start, sbyte amount)
    {
        var total = start;
        checked
        {
            Action accumulate = () => total += amount;

            accumulate();
        }

        return total;
    }

    /// <summary>The value stored through a receiver with a side effect, plus 1000 per evaluation of the receiver.</summary>
    public static int ReceiverEvaluatedOnce(byte start, byte amount)
    {
        var source = new NarrowSource(start);

        source.Next().Value += amount;

        return source.Evaluations * 1000 + source.Value;
    }

    public static int ReceiverIncrementedOnce(sbyte start)
    {
        var source = new NarrowSource(0) { Signed = start };
        var previous = source.Next().Signed++;

        return source.Evaluations * 1000 + (previous ^ source.Signed);
    }

    public static int ForeachBytes(int[] values)
    {
        var total = 0;

        foreach (byte value in values)
            total += value;

        return total;
    }

    public static int ForeachBytesChecked(int[] values)
    {
        var total = 0;
        checked
        {
            foreach (byte value in values)
                total += value;
        }

        return total;
    }

    public static int TupleLiteralCast(int a, int b)
    {
        var narrow = ((byte, short))(a, b);

        return narrow.Item1 * 100000 + narrow.Item2;
    }

    public static int TupleCast(int a, int b)
    {
        (int, int) wide = (a, b);
        var narrow = ((sbyte, ushort))wide;

        return narrow.Item1 * 100000 + narrow.Item2;
    }

    public static int TupleCastChecked(int a, int b)
    {
        (int, int) wide = (a, b);
        checked
        {
            var narrow = ((sbyte, ushort))wide;

            return narrow.Item1 * 100000 + narrow.Item2;
        }
    }

    public static short UncheckedInsideCheckedCast(int source) =>
        checked((short)(unchecked((byte)source) * 200));

    public static int CheckedInsideUncheckedCast(int source) =>
        unchecked((sbyte)(checked((ushort)source) >> 4));

    public static int NestedBlocks(int source)
    {
        checked
        {
            var low = unchecked((byte)source);
            unchecked
            {
                var wrapped = (sbyte)low;
                checked
                {
                    return (byte)(wrapped + 128) + (ushort)source;
                }
            }
        }
    }

    public static ushort ShiftedByteToUShort(byte a) => (ushort)(a << 8);

    public static ushort ShiftedByteToUShortReduced(byte a) => (ushort)(a << 9);

    public static short ShiftedByteToShort(byte a) => (short)(a << 7);

    public static short ShiftedByteToShortReduced(byte a) => (short)(a << 8);

    public static byte NibblePack(byte a, byte b) => (byte)((a & 0xF) << 4 | (b & 0xF));

    public static byte NibblePackReduced(byte a, byte b) => (byte)((a & 0x1F) << 4 | (b & 0xF));

    public static uint UIntShiftedBelowBit31(uint source) => (source & 0x7F) << 24;

    public static uint UIntShiftedIntoBit31(uint source) => (source & 0xFF) << 24;

    public static uint ByteShiftedBelowBit31(byte a) => (uint)a << 23;

    public static uint ByteShiftedIntoBit31(byte a) => (uint)a << 24;

    public static int ShiftedBytesSumChecked(byte a, byte b) => checked((a << 8) + b);

    public static int ShiftedIntSumChecked(int source, byte b) => checked((source << 8) + b);

    public static int ShiftBy31(int source) => source << 31;

    public static int ShiftBy32(int source) => source << 32;

    public static int ShiftBy33(int source) => source << 33;

    public static int ShiftByMinusOne(int source) => source << -1;

    public static uint UIntShiftBy31(uint source) => source << 31;

    public static uint UIntShiftBy32(uint source) => source << 32;

    public static uint UIntShiftBy33(uint source) => source << 33;

    public static uint UIntShiftRightByMinusOne(uint source) => source >> -1;

    public static string ShiftConstants() =>
        $"{1 << 31}|{1 << 32}|{1 << 33}|{1 << -1}|{1u << 31}|{1u << 32}|{3 << 30}|{1 << 4}";

    public static int AddShiftConstant(int source) => source / 2 + (1 << 32) + (1 << 4);

    public static byte AndNegativeToByte(int source) => (byte)(source & -256);

    public static sbyte OrNegativeToSByte(sbyte a) => (sbyte)(a | -64);

    public static short XorNegativeToShort(short a) => (short)(a ^ -1);

    public static uint OrNegativeToUInt(int source) => (uint)(source | -2147483648);

    public static int AndNegativeOperands(int source, short b) => source & b;

    public static byte ULongProductToByte(ulong a, ulong b) => (byte)(int)(a * b);

    /// <summary>The stored value and how often the receiver was evaluated.</summary>
    public static string ReceiverShiftedLeft(int start, int count)
    {
        var cells = new ShiftCells(start);

        cells.Next().Signed <<= count;

        return $"{cells.Signed}|{cells.Evaluations}";
    }

    public static string ReceiverShiftedRight(int start, int count)
    {
        var cells = new ShiftCells(start);

        cells.Next().Signed >>= count;

        return $"{cells.Signed}|{cells.Evaluations}";
    }

    public static string ReceiverShiftedRightLogical(int start, int count)
    {
        var cells = new ShiftCells(start);

        cells.Next().Signed >>>= count;

        return $"{cells.Signed}|{cells.Evaluations}";
    }

    public static string ReceiverUnsignedShiftedRight(uint start, int count)
    {
        var cells = new ShiftCells(0) { Unsigned = start };

        cells.Next().Unsigned >>= count;

        return $"{cells.Unsigned}|{cells.Evaluations}";
    }

    public static string ReceiverUnsignedShiftedLeft(uint start, int count)
    {
        var cells = new ShiftCells(0) { Unsigned = start };

        cells.Next().Unsigned <<= count;

        return $"{cells.Unsigned}|{cells.Evaluations}";
    }

    public static string ReceiverByteShiftedLeft(byte start, int count)
    {
        var cells = new ShiftCells(0) { Small = start };

        cells.Next().Small <<= count;

        return $"{cells.Small}|{cells.Evaluations}";
    }

    /// <summary>The stored element, the receiver evaluations and the index after its increment.</summary>
    public static string ReceiverElementShifted(int start, int count)
    {
        var cells = new ShiftCells(start);
        var index = 0;

        cells.Next().Slots[index++] <<= count;

        return $"{cells.Slots[0]}|{cells.Evaluations}|{index}";
    }

    /// <summary>The stored element and how often the index and the count were evaluated.</summary>
    public static string IndexAndCountEvaluatedOnce(int start, int count)
    {
        var cells = new ShiftCells(start);

        cells.Slots[cells.NextIndex()] <<= cells.NextCount(count);

        return $"{cells.Slots[1]}|{cells.IndexEvaluations}|{cells.CountEvaluations}";
    }

    /// <summary>The stored property value, the receiver evaluations and the getter and setter calls.</summary>
    public static string ReceiverPropertyShifted(int start, int count)
    {
        var cells = new ShiftCells(start);

        cells.Next().Tracked <<= count;

        return $"{cells.Signed}|{cells.Evaluations}|{cells.Reads}|{cells.Writes}";
    }

    public static string ArrayElementReceiverShifted(int start)
    {
        var all = new[] { new ShiftCells(start), new ShiftCells(start) };

        all[0].Slots[1] <<= 2;

        return $"{all[0].Slots[1]}|{all[1].Slots[1]}";
    }

    public static async Task<string> ReceiverShiftedByAwaitedCount(int start, int count)
    {
        var cells = new ShiftCells(start);

        cells.Next().Signed <<= await Task.FromResult(count);

        return $"{cells.Signed}|{cells.Evaluations}";
    }

    public static string ConcatenatedChecked(int source) => "v=" + checked((byte)source) + ";";

    public static string InterpolatedChecked(int a, int b) =>
        $"{checked(a + b)}|{unchecked((byte)a)}|{checked((sbyte)(b & 0x7F))}";
}

/// <summary>
/// The static fields <see cref="NarrowShapes"/> updates in place. Emitted
/// beside the functions that use it, so the generated module has no relative
/// import and runs under Node's type stripping as it is.
/// </summary>
[EmitInFile("narrow-shapes")]
public static class NarrowStatics
{
    public static byte Count;

    public static short Level;
}

/// <summary>
/// A receiver whose read has a side effect: <see cref="Next"/> counts its
/// calls, so a compound assignment through it shows how often the lowering
/// evaluated the receiver. Emitted beside <see cref="NarrowShapes"/>.
/// </summary>
[EmitInFile("narrow-shapes")]
public class NarrowSource
{
    public byte Value;

    public sbyte Signed;

    public NarrowSource(byte value)
    {
        Value = value;
    }

    public int Evaluations { get; private set; }

    public NarrowSource Next()
    {
        Evaluations++;

        return this;
    }
}

/// <summary>
/// The targets of the compound shifts of <see cref="NarrowShapes"/>:
/// <see cref="Next"/>, <see cref="NextIndex"/>, <see cref="NextCount"/> and
/// the accessors of <see cref="Tracked"/> count their calls, so a row shows
/// that the lowering evaluated the receiver, the index, the count, the
/// getter and the setter once each. Emitted beside the functions that use it.
/// </summary>
[EmitInFile("narrow-shapes")]
public class ShiftCells
{
    public int Signed;

    public uint Unsigned;

    public byte Small;

    public int[] Slots;

    public ShiftCells(int seed)
    {
        Signed = seed;
        Slots = new[] { seed, seed };
    }

    public int Evaluations { get; private set; }

    public int IndexEvaluations { get; private set; }

    public int CountEvaluations { get; private set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public int Tracked
    {
        get
        {
            Reads++;

            return Signed;
        }
        set
        {
            Writes++;
            Signed = value;
        }
    }

    public ShiftCells Next()
    {
        Evaluations++;

        return this;
    }

    public int NextIndex()
    {
        IndexEvaluations++;

        return 1;
    }

    public int NextCount(int count)
    {
        CountEvaluations++;

        return count;
    }
}
