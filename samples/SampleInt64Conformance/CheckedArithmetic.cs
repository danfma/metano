using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// <c>int</c> and <c>uint</c> arithmetic in a checked context (spec 012
/// FR-077, issue #639): <c>+</c>, <c>-</c>, <c>*</c>, unary <c>-</c>,
/// <c>++</c>, <c>--</c> and their compound forms throw
/// <c>OverflowException</c> outside the range. The types narrower than
/// <c>int</c> promote to <c>int</c>, so only an <c>int</c> overflow or the
/// final narrowing throws. Unchecked <c>int</c> / <c>uint</c> arithmetic is
/// not in this matrix: it does not wrap in the generated code (the
/// documented gap).
/// </summary>
[NoContainer]
public static class CheckedArithmetic
{
    public static int IntAddChecked(int a, int b) => checked(a + b);

    public static int IntCompoundAddChecked(int a, int b)
    {
        checked
        {
            a += b;
        }

        return a;
    }

    public static int IntSubtractChecked(int a, int b) => checked(a - b);

    public static int IntCompoundSubtractChecked(int a, int b)
    {
        checked
        {
            a -= b;
        }

        return a;
    }

    public static int IntMultiplyChecked(int a, int b) => checked(a * b);

    public static int IntCompoundMultiplyChecked(int a, int b)
    {
        checked
        {
            a *= b;
        }

        return a;
    }

    public static int IntPrefixIncrementChecked(int a) => checked(++a);

    public static int IntPostfixIncrementChecked(int a)
    {
        checked
        {
            var previous = a++;

            return previous;
        }
    }

    public static int IntPostfixIncrementStoredChecked(int a)
    {
        checked
        {
            a++;
        }

        return a;
    }

    public static int IntPrefixDecrementChecked(int a) => checked(--a);

    public static int IntPostfixDecrementChecked(int a)
    {
        checked
        {
            var previous = a--;

            return previous;
        }
    }

    public static int IntPostfixDecrementStoredChecked(int a)
    {
        checked
        {
            a--;
        }

        return a;
    }

    public static uint UIntAddChecked(uint a, uint b) => checked(a + b);

    public static uint UIntCompoundAddChecked(uint a, uint b)
    {
        checked
        {
            a += b;
        }

        return a;
    }

    public static uint UIntSubtractChecked(uint a, uint b) => checked(a - b);

    public static uint UIntCompoundSubtractChecked(uint a, uint b)
    {
        checked
        {
            a -= b;
        }

        return a;
    }

    public static uint UIntMultiplyChecked(uint a, uint b) => checked(a * b);

    public static uint UIntCompoundMultiplyChecked(uint a, uint b)
    {
        checked
        {
            a *= b;
        }

        return a;
    }

    public static uint UIntPrefixIncrementChecked(uint a) => checked(++a);

    public static uint UIntPostfixIncrementChecked(uint a)
    {
        checked
        {
            var previous = a++;

            return previous;
        }
    }

    public static uint UIntPostfixIncrementStoredChecked(uint a)
    {
        checked
        {
            a++;
        }

        return a;
    }

    public static uint UIntPrefixDecrementChecked(uint a) => checked(--a);

    public static uint UIntPostfixDecrementChecked(uint a)
    {
        checked
        {
            var previous = a--;

            return previous;
        }
    }

    public static uint UIntPostfixDecrementStoredChecked(uint a)
    {
        checked
        {
            a--;
        }

        return a;
    }

    public static int IntNegateChecked(int a) => checked(-a);

    public static int IntShiftLeftChecked(int a, int count) => checked(a << count);

    public static int BytesAddChecked(byte a, byte b) => checked(a + b);

    public static int ShortsMultiplyChecked(short a, short b) => checked(a * b);

    public static int UShortsMultiplyChecked(ushort a, ushort b) => checked(a * b);

    public static int ShortNegateChecked(short a) => checked(-a);

    public static byte BytesAddNarrowedChecked(byte a, byte b) => checked((byte)(a + b));

    public static short ShortsMultiplyNarrowedChecked(short a, short b) => checked((short)(a * b));

    public static int? IntLiftedAddChecked(int? a, int? b) => checked(a + b);

    public static int? IntLiftedNegateChecked(int? a) => checked(-a);

    public static int IntUncheckedInsideChecked(int a) => checked(a + unchecked((byte)a));

    public static int IntCheckedLambda(int a, int b)
    {
        checked
        {
            Func<int, int> scale = value => value * b;

            return scale(a);
        }
    }

    public static int IntFieldCompoundChecked(int start, int amount)
    {
        var tally = new Tally(start);
        checked
        {
            tally.Total += amount;
            tally.Total++;
        }

        return tally.Total;
    }
}

/// <summary>
/// The <c>int</c> field <see cref="CheckedArithmetic.IntFieldCompoundChecked"/>
/// updates in place. Emitted beside the functions that use it, so the
/// generated module has no relative import and runs under Node's type
/// stripping as it is.
/// </summary>
[EmitInFile("checked-arithmetic")]
public class Tally
{
    public int Total;

    public Tally(int total)
    {
        Total = total;
    }
}
