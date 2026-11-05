using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The shift and bitwise rows of the reference matrix (spec 012
/// FR-028–FR-030, contracts/ts-emission.md § Operators): counts masked with
/// <c>63</c> (counts of 64 or more and negative counts included),
/// <c>&lt;&lt;</c> wrapping in either context, arithmetic <c>&gt;&gt;</c> on
/// <c>long</c>, logical on <c>ulong</c>, <c>&gt;&gt;&gt;</c>, lifted and
/// compound forms.
/// </summary>
[NoContainer]
public static class Shifts
{
    public static long ShiftLeft(long a, int n) => a << n;

    public static long ShiftRight(long a, int n) => a >> n;

    public static long ShiftRightLogical(long a, int n) => a >>> n;

    public static ulong ShiftLeftUnsigned(ulong a, int n) => a << n;

    public static ulong ShiftRightUnsigned(ulong a, int n) => a >> n;

    public static ulong ShiftRightLogicalUnsigned(ulong a, int n) => a >>> n;

    public static long ShiftLeftChecked(long a, int n) => checked(a << n);

    public static long ShiftLeftBySeventy(long a) => a << 70;

    public static long ShiftRightByMinusOne(long a) => a >> -1;

    public static long ShiftRightLogicalByTwo(long a) => a >>> 2;

    public static long? LiftedShiftLeft(long? a, int? n) => a << n;

    public static long CompoundShiftLeft(long a, int n)
    {
        var x = a;
        x <<= n;

        return x;
    }

    public static long CompoundShiftLeftChecked(long a, int n)
    {
        var x = a;
        checked
        {
            x <<= n;
        }

        return x;
    }

    public static long CompoundShiftRightLogical(long a, int n)
    {
        var x = a;
        x >>>= n;

        return x;
    }

    public static ulong CompoundShiftRightUnsigned(ulong a, int n)
    {
        var x = a;
        x >>= n;

        return x;
    }

    public static long Bitwise(long a, long b) => (a & b) | (a ^ ~b);

    public static ulong BitwiseUnsigned(ulong a, ulong b) => (a & b) | (a ^ ~b);
}
