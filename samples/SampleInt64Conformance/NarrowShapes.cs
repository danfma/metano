using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The shapes around the integers of 32 bits or fewer that the conversion,
/// store and operator matrices do not reach (spec 012 FR-074–FR-077, issue
/// #639): operands whose values the target holds, so the narrowing is
/// omitted, next to ones where it is not; nested casts; static fields,
/// captured variables and a receiver with a side effect; the element
/// conversion of a <c>foreach</c>; tuple casts; nested <c>checked</c> /
/// <c>unchecked</c> contexts; and a checked expression inside a string.
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
