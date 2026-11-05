using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The <c>Math</c> and <c>INumber</c> rows of the reference matrix (spec 012
/// FR-035, contracts/runtime-int64.md § Math): the 64-bit overloads of
/// <c>Abs</c>, <c>Max</c>, <c>Min</c>, <c>Sign</c>, <c>Clamp</c>,
/// <c>DivRem</c> and <c>BigMul</c>, with .NET's exceptions. A pair result is
/// joined into one string so the fixture compares it as text.
/// </summary>
[NoContainer]
public static class MathStatics
{
    public static long Abs(long value) => Math.Abs(value);

    public static long AbsStatic(long value) => long.Abs(value);

    public static long Max(long a, long b) => Math.Max(a, b);

    public static long Min(long a, long b) => long.Min(a, b);

    public static long MaxWidened(int a, long b) => Math.Max(a, b);

    public static ulong MaxUnsigned(ulong a, ulong b) => ulong.Max(a, b);

    public static ulong MinUnsigned(ulong a, ulong b) => Math.Min(a, b);

    public static int Sign(long value) => Math.Sign(value);

    public static int SignUnsigned(ulong value) => ulong.Sign(value);

    public static long Clamp(long value, long min, long max) => Math.Clamp(value, min, max);

    public static ulong ClampUnsigned(ulong value, ulong min, ulong max) =>
        ulong.Clamp(value, min, max);

    public static string DivRem(long a, long b)
    {
        var pair = Math.DivRem(a, b);

        return pair.Item1 + "," + pair.Item2;
    }

    public static string DivRemOut(long a, long b)
    {
        var quotient = Math.DivRem(a, b, out long modulus);

        return quotient + "," + modulus;
    }

    public static string DivRemUnsigned(ulong a, ulong b)
    {
        var pair = ulong.DivRem(a, b);

        return pair.Item1 + "," + pair.Item2;
    }

    public static string BigMul(long a, long b)
    {
        var high = Math.BigMul(a, b, out long low);

        return high + "," + low;
    }

    public static string BigMulUnsigned(ulong a, ulong b)
    {
        var high = Math.BigMul(a, b, out ulong low);

        return high + "," + low;
    }

    public static double Pow(long value) => Math.Pow(value, 2);
}
