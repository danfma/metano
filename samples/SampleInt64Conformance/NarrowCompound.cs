using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// Compound assignments and increments on <c>sbyte</c>, <c>byte</c>,
/// <c>short</c> and <c>ushort</c> (spec 012 FR-075, FR-077, issue #639):
/// C# computes in <c>int</c> and stores through the narrowing it inserts
/// (<c>b += 1</c> is <c>b = (byte)(b + 1)</c>), on locals, fields,
/// properties and array elements, unchecked and checked. A parameter named
/// <c>divisor</c> gets non-zero operands.
/// </summary>
[NoContainer]
public static class NarrowCompound
{
    public static sbyte SByteAdd(sbyte a, sbyte b)
    {
        a += b;

        return a;
    }

    public static sbyte SByteSubtract(sbyte a, sbyte b)
    {
        a -= b;

        return a;
    }

    public static sbyte SByteMultiply(sbyte a, sbyte b)
    {
        a *= b;

        return a;
    }

    public static sbyte SByteAnd(sbyte a, sbyte b)
    {
        a &= b;

        return a;
    }

    public static sbyte SByteOr(sbyte a, sbyte b)
    {
        a |= b;

        return a;
    }

    public static sbyte SByteXor(sbyte a, sbyte b)
    {
        a ^= b;

        return a;
    }

    public static sbyte SByteAddChecked(sbyte a, sbyte b)
    {
        checked
        {
            a += b;
        }

        return a;
    }

    public static sbyte SByteSubtractChecked(sbyte a, sbyte b)
    {
        checked
        {
            a -= b;
        }

        return a;
    }

    public static sbyte SByteMultiplyChecked(sbyte a, sbyte b)
    {
        checked
        {
            a *= b;
        }

        return a;
    }

    public static sbyte SByteDivide(sbyte a, sbyte divisor)
    {
        a /= divisor;

        return a;
    }

    public static sbyte SByteRemainder(sbyte a, sbyte divisor)
    {
        a %= divisor;

        return a;
    }

    public static sbyte SByteDivideChecked(sbyte a, sbyte divisor)
    {
        checked
        {
            a /= divisor;
        }

        return a;
    }

    public static sbyte SByteShiftLeft(sbyte a, int count)
    {
        a <<= count;

        return a;
    }

    public static sbyte SByteShiftLeftChecked(sbyte a, int count)
    {
        checked
        {
            a <<= count;
        }

        return a;
    }

    public static sbyte SByteShiftRight(sbyte a, int count)
    {
        a >>= count;

        return a;
    }

    public static sbyte SByteShiftRightChecked(sbyte a, int count)
    {
        checked
        {
            a >>= count;
        }

        return a;
    }

    public static sbyte SByteShiftRightLogical(sbyte a, int count)
    {
        a >>>= count;

        return a;
    }

    public static sbyte SByteShiftRightLogicalChecked(sbyte a, int count)
    {
        checked
        {
            a >>>= count;
        }

        return a;
    }

    public static sbyte SBytePrefixIncrement(sbyte a) => ++a;

    public static sbyte SBytePostfixIncrement(sbyte a)
    {
        var previous = a++;

        return previous;
    }

    public static sbyte SBytePostfixIncrementStored(sbyte a)
    {
        a++;

        return a;
    }

    public static sbyte SBytePrefixIncrementChecked(sbyte a) => checked(++a);

    public static sbyte SBytePostfixIncrementChecked(sbyte a)
    {
        checked
        {
            var previous = a++;

            return previous;
        }
    }

    public static sbyte SBytePrefixDecrement(sbyte a) => --a;

    public static sbyte SBytePostfixDecrement(sbyte a)
    {
        var previous = a--;

        return previous;
    }

    public static sbyte SBytePostfixDecrementStored(sbyte a)
    {
        a--;

        return a;
    }

    public static sbyte SBytePrefixDecrementChecked(sbyte a) => checked(--a);

    public static sbyte SBytePostfixDecrementChecked(sbyte a)
    {
        checked
        {
            var previous = a--;

            return previous;
        }
    }

    public static byte ByteAdd(byte a, byte b)
    {
        a += b;

        return a;
    }

    public static byte ByteSubtract(byte a, byte b)
    {
        a -= b;

        return a;
    }

    public static byte ByteMultiply(byte a, byte b)
    {
        a *= b;

        return a;
    }

    public static byte ByteAnd(byte a, byte b)
    {
        a &= b;

        return a;
    }

    public static byte ByteOr(byte a, byte b)
    {
        a |= b;

        return a;
    }

    public static byte ByteXor(byte a, byte b)
    {
        a ^= b;

        return a;
    }

    public static byte ByteAddChecked(byte a, byte b)
    {
        checked
        {
            a += b;
        }

        return a;
    }

    public static byte ByteSubtractChecked(byte a, byte b)
    {
        checked
        {
            a -= b;
        }

        return a;
    }

    public static byte ByteMultiplyChecked(byte a, byte b)
    {
        checked
        {
            a *= b;
        }

        return a;
    }

    public static byte ByteDivide(byte a, byte divisor)
    {
        a /= divisor;

        return a;
    }

    public static byte ByteRemainder(byte a, byte divisor)
    {
        a %= divisor;

        return a;
    }

    public static byte ByteDivideChecked(byte a, byte divisor)
    {
        checked
        {
            a /= divisor;
        }

        return a;
    }

    public static byte ByteShiftLeft(byte a, int count)
    {
        a <<= count;

        return a;
    }

    public static byte ByteShiftLeftChecked(byte a, int count)
    {
        checked
        {
            a <<= count;
        }

        return a;
    }

    public static byte ByteShiftRight(byte a, int count)
    {
        a >>= count;

        return a;
    }

    public static byte ByteShiftRightChecked(byte a, int count)
    {
        checked
        {
            a >>= count;
        }

        return a;
    }

    public static byte ByteShiftRightLogical(byte a, int count)
    {
        a >>>= count;

        return a;
    }

    public static byte ByteShiftRightLogicalChecked(byte a, int count)
    {
        checked
        {
            a >>>= count;
        }

        return a;
    }

    public static byte BytePrefixIncrement(byte a) => ++a;

    public static byte BytePostfixIncrement(byte a)
    {
        var previous = a++;

        return previous;
    }

    public static byte BytePostfixIncrementStored(byte a)
    {
        a++;

        return a;
    }

    public static byte BytePrefixIncrementChecked(byte a) => checked(++a);

    public static byte BytePostfixIncrementChecked(byte a)
    {
        checked
        {
            var previous = a++;

            return previous;
        }
    }

    public static byte BytePrefixDecrement(byte a) => --a;

    public static byte BytePostfixDecrement(byte a)
    {
        var previous = a--;

        return previous;
    }

    public static byte BytePostfixDecrementStored(byte a)
    {
        a--;

        return a;
    }

    public static byte BytePrefixDecrementChecked(byte a) => checked(--a);

    public static byte BytePostfixDecrementChecked(byte a)
    {
        checked
        {
            var previous = a--;

            return previous;
        }
    }

    public static short ShortAdd(short a, short b)
    {
        a += b;

        return a;
    }

    public static short ShortSubtract(short a, short b)
    {
        a -= b;

        return a;
    }

    public static short ShortMultiply(short a, short b)
    {
        a *= b;

        return a;
    }

    public static short ShortAnd(short a, short b)
    {
        a &= b;

        return a;
    }

    public static short ShortOr(short a, short b)
    {
        a |= b;

        return a;
    }

    public static short ShortXor(short a, short b)
    {
        a ^= b;

        return a;
    }

    public static short ShortAddChecked(short a, short b)
    {
        checked
        {
            a += b;
        }

        return a;
    }

    public static short ShortSubtractChecked(short a, short b)
    {
        checked
        {
            a -= b;
        }

        return a;
    }

    public static short ShortMultiplyChecked(short a, short b)
    {
        checked
        {
            a *= b;
        }

        return a;
    }

    public static short ShortDivide(short a, short divisor)
    {
        a /= divisor;

        return a;
    }

    public static short ShortRemainder(short a, short divisor)
    {
        a %= divisor;

        return a;
    }

    public static short ShortDivideChecked(short a, short divisor)
    {
        checked
        {
            a /= divisor;
        }

        return a;
    }

    public static short ShortShiftLeft(short a, int count)
    {
        a <<= count;

        return a;
    }

    public static short ShortShiftLeftChecked(short a, int count)
    {
        checked
        {
            a <<= count;
        }

        return a;
    }

    public static short ShortShiftRight(short a, int count)
    {
        a >>= count;

        return a;
    }

    public static short ShortShiftRightChecked(short a, int count)
    {
        checked
        {
            a >>= count;
        }

        return a;
    }

    public static short ShortShiftRightLogical(short a, int count)
    {
        a >>>= count;

        return a;
    }

    public static short ShortShiftRightLogicalChecked(short a, int count)
    {
        checked
        {
            a >>>= count;
        }

        return a;
    }

    public static short ShortPrefixIncrement(short a) => ++a;

    public static short ShortPostfixIncrement(short a)
    {
        var previous = a++;

        return previous;
    }

    public static short ShortPostfixIncrementStored(short a)
    {
        a++;

        return a;
    }

    public static short ShortPrefixIncrementChecked(short a) => checked(++a);

    public static short ShortPostfixIncrementChecked(short a)
    {
        checked
        {
            var previous = a++;

            return previous;
        }
    }

    public static short ShortPrefixDecrement(short a) => --a;

    public static short ShortPostfixDecrement(short a)
    {
        var previous = a--;

        return previous;
    }

    public static short ShortPostfixDecrementStored(short a)
    {
        a--;

        return a;
    }

    public static short ShortPrefixDecrementChecked(short a) => checked(--a);

    public static short ShortPostfixDecrementChecked(short a)
    {
        checked
        {
            var previous = a--;

            return previous;
        }
    }

    public static ushort UShortAdd(ushort a, ushort b)
    {
        a += b;

        return a;
    }

    public static ushort UShortSubtract(ushort a, ushort b)
    {
        a -= b;

        return a;
    }

    public static ushort UShortMultiply(ushort a, ushort b)
    {
        a *= b;

        return a;
    }

    public static ushort UShortAnd(ushort a, ushort b)
    {
        a &= b;

        return a;
    }

    public static ushort UShortOr(ushort a, ushort b)
    {
        a |= b;

        return a;
    }

    public static ushort UShortXor(ushort a, ushort b)
    {
        a ^= b;

        return a;
    }

    public static ushort UShortAddChecked(ushort a, ushort b)
    {
        checked
        {
            a += b;
        }

        return a;
    }

    public static ushort UShortSubtractChecked(ushort a, ushort b)
    {
        checked
        {
            a -= b;
        }

        return a;
    }

    public static ushort UShortMultiplyChecked(ushort a, ushort b)
    {
        checked
        {
            a *= b;
        }

        return a;
    }

    public static ushort UShortDivide(ushort a, ushort divisor)
    {
        a /= divisor;

        return a;
    }

    public static ushort UShortRemainder(ushort a, ushort divisor)
    {
        a %= divisor;

        return a;
    }

    public static ushort UShortDivideChecked(ushort a, ushort divisor)
    {
        checked
        {
            a /= divisor;
        }

        return a;
    }

    public static ushort UShortShiftLeft(ushort a, int count)
    {
        a <<= count;

        return a;
    }

    public static ushort UShortShiftLeftChecked(ushort a, int count)
    {
        checked
        {
            a <<= count;
        }

        return a;
    }

    public static ushort UShortShiftRight(ushort a, int count)
    {
        a >>= count;

        return a;
    }

    public static ushort UShortShiftRightChecked(ushort a, int count)
    {
        checked
        {
            a >>= count;
        }

        return a;
    }

    public static ushort UShortShiftRightLogical(ushort a, int count)
    {
        a >>>= count;

        return a;
    }

    public static ushort UShortShiftRightLogicalChecked(ushort a, int count)
    {
        checked
        {
            a >>>= count;
        }

        return a;
    }

    public static ushort UShortPrefixIncrement(ushort a) => ++a;

    public static ushort UShortPostfixIncrement(ushort a)
    {
        var previous = a++;

        return previous;
    }

    public static ushort UShortPostfixIncrementStored(ushort a)
    {
        a++;

        return a;
    }

    public static ushort UShortPrefixIncrementChecked(ushort a) => checked(++a);

    public static ushort UShortPostfixIncrementChecked(ushort a)
    {
        checked
        {
            var previous = a++;

            return previous;
        }
    }

    public static ushort UShortPrefixDecrement(ushort a) => --a;

    public static ushort UShortPostfixDecrement(ushort a)
    {
        var previous = a--;

        return previous;
    }

    public static ushort UShortPostfixDecrementStored(ushort a)
    {
        a--;

        return a;
    }

    public static ushort UShortPrefixDecrementChecked(ushort a) => checked(--a);

    public static ushort UShortPostfixDecrementChecked(ushort a)
    {
        checked
        {
            var previous = a--;

            return previous;
        }
    }

    public static byte ByteFieldCompound(byte start, byte amount)
    {
        var cells = new NarrowCells(0);

        cells.ByteField = start;
        cells.ByteField += amount;
        cells.ByteField++;

        return cells.ByteField;
    }

    public static sbyte SByteFieldCompoundChecked(sbyte start, sbyte amount)
    {
        var cells = new NarrowCells(0);

        cells.SByteField = start;
        checked
        {
            cells.SByteField -= amount;
            cells.SByteField--;
        }

        return cells.SByteField;
    }

    public static short ShortPropertyCompound(short start, short amount)
    {
        var cells = new NarrowCells(0);

        cells.ShortProperty = start;
        cells.ShortProperty *= amount;
        --cells.ShortProperty;

        return cells.ShortProperty;
    }

    public static short ShortPropertyPostfix(short start)
    {
        var cells = new NarrowCells(0);

        cells.ShortProperty = start;

        var previous = cells.ShortProperty++;

        return (short)(previous ^ cells.ShortProperty);
    }

    public static ushort UShortElementCompound(ushort start, ushort amount) =>
        new NarrowCells(start).AddToCell(amount);

    public static ushort UShortElementPostfix(ushort start) =>
        new NarrowCells(start).IncrementCell();

    public static ushort UShortElementShiftChecked(ushort start, int count) =>
        new NarrowCells(start).ShiftCellChecked(count);

    public static byte? ByteLiftedIncrement(byte? a)
    {
        a++;

        return a;
    }

    public static short? ShortLiftedDecrementChecked(short? a)
    {
        checked
        {
            a--;
        }

        return a;
    }

    /// <summary>The steps a <c>byte</c> counter takes to wrap back to zero, capped at 300.</summary>
    public static int StepsUntilWrap(byte start)
    {
        var steps = 0;

        for (var i = start; steps < 300; i++)
        {
            if (i == 0 && steps > 0)
                break;

            steps++;
        }

        return steps;
    }
}

/// <summary>
/// The field, property and array-element targets of <see cref="NarrowCompound"/>;
/// the index of the element has a side effect the read-modify-write lowering
/// must evaluate once. Emitted beside the functions that use it, so the
/// generated module has no relative import and runs under Node's type
/// stripping as it is.
/// </summary>
[EmitInFile("narrow-compound")]
public class NarrowCells
{
    private const ushort NotReadOnce = 0xFFFF;

    private readonly ushort[] _cells;

    private int _cellReads;

    public byte ByteField;

    public sbyte SByteField;

    public NarrowCells(ushort seed)
    {
        _cells = new ushort[] { 0, seed };
    }

    public short ShortProperty { get; set; }

    /// <summary>The sum stored in the cell, or <c>0xFFFF</c> when the index was not read exactly once.</summary>
    public ushort AddToCell(ushort amount)
    {
        _cells[NextCell()] += amount;

        return _cellReads == 1 ? _cells[1] : NotReadOnce;
    }

    /// <summary>The value before the increment xor the stored one, or <c>0xFFFF</c> when the index was not read exactly once.</summary>
    public ushort IncrementCell()
    {
        var previous = _cells[NextCell()]++;

        return _cellReads == 1 ? (ushort)(previous ^ _cells[1]) : NotReadOnce;
    }

    public ushort ShiftCellChecked(int count)
    {
        checked
        {
            _cells[NextCell()] <<= count;
        }

        return _cells[1];
    }

    private int NextCell()
    {
        _cellReads++;

        return 1;
    }
}
