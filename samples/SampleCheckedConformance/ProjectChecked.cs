using System;
using Metano.Annotations;

namespace SampleCheckedConformance;

/// <summary>
/// The rows of a project compiled with <c>CheckForOverflowUnderflow</c>
/// (spec 012 DECIDED-4, FR-023, FR-077, issue #639): no method here names
/// <c>checked</c>, yet every conversion and every arithmetic operation
/// outside an <c>unchecked</c> context throws <c>OverflowException</c>
/// outside its range, for the integers of 32 bits or fewer and for the
/// 64-bit ones. tests/Int64Reference runs every public method over boundary
/// operands on .NET; the transpiled twin must reproduce each row.
/// </summary>
[NoContainer]
public static class ProjectChecked
{
    public static int IntAdd(int a, int b) => a + b;

    public static int IntSubtract(int a, int b) => a - b;

    public static int IntMultiply(int a, int b) => a * b;

    public static int IntNegate(int a) => -a;

    public static uint UIntAdd(uint a, uint b) => a + b;

    public static uint UIntSubtract(uint a, uint b) => a - b;

    public static int IntIncrement(int a)
    {
        a++;

        return a;
    }

    public static uint UIntDecrement(uint a)
    {
        var previous = a--;

        return previous;
    }

    public static int IntShiftLeft(int a, int count) => a << count;

    public static uint UIntOr(uint a, uint b) => a | b;

    public static byte IntToByte(int source) => (byte)source;

    public static sbyte IntToSByte(int source) => (sbyte)source;

    public static short IntToShort(int source) => (short)source;

    public static ushort IntToUShort(int source) => (ushort)source;

    public static uint IntToUInt(int source) => (uint)source;

    public static int UIntToInt(uint source) => (int)source;

    public static byte SByteToByte(sbyte source) => (byte)source;

    public static int LongToInt(long source) => (int)source;

    public static byte DoubleToByte(double source) => (byte)source;

    public static byte? LiftedIntToByte(int? source) => (byte?)source;

    public static byte ByteAdd(byte a, byte b)
    {
        a += b;

        return a;
    }

    public static sbyte SByteIncrement(sbyte a) => ++a;

    public static short ShortPostfixDecrement(short a)
    {
        var previous = a--;

        return (short)(previous ^ a);
    }

    public static ushort UShortShiftLeft(ushort a, int count)
    {
        a <<= count;

        return a;
    }

    public static int BytesAdd(byte a, byte b) => a + b;

    public static long LongAdd(long a, long b) => a + b;

    public static ulong ULongSubtract(ulong a, ulong b) => a - b;

    public static int IntAddLambda(int a, int b)
    {
        Func<int, int> add = value => value + b;

        return add(a);
    }

    public static byte UncheckedIntToByte(int source) => unchecked((byte)source);

    public static uint UncheckedIntToUInt(int source) => unchecked((uint)source);

    public static byte UncheckedByteAdd(byte a, byte b)
    {
        unchecked
        {
            a += b;
        }

        return a;
    }

    public static long UncheckedLongAdd(long a, long b) => unchecked(a + b);
}
