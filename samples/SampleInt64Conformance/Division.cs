using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The division and remainder rows of the reference matrix (spec 012
/// FR-027, contracts/ts-emission.md § Operators): truncation toward zero,
/// the dividend's sign, <c>DivideByZeroException</c> and the
/// <c>long.MinValue / -1</c> <c>OverflowException</c> in either context,
/// constant divisors, lifted and compound forms.
/// </summary>
[NoContainer]
public static class Division
{
    public static long Divide(long a, long b) => a / b;

    public static long Remainder(long a, long b) => a % b;

    public static long DivideUnchecked(long a, long b) => unchecked(a / b);

    public static long RemainderChecked(long a, long b) => checked(a % b);

    public static ulong DivideUnsigned(ulong a, ulong b) => a / b;

    public static ulong RemainderUnsigned(ulong a, ulong b) => a % b;

    public static long DivideInt(long a, int b) => a / b;

    public static long DivideBySeven(long a) => a / 7;

    public static long RemainderByMinusSeven(long a) => a % -7;

    public static long DivideByMinusOne(long a) => a / -1;

    public static long RemainderByMinusOne(long a) => a % -1;

    public static ulong DivideUnsignedByThree(ulong a) => a / 3;

    public static long? LiftedDivide(long? a, long? b) => a / b;

    public static ulong? LiftedRemainderUnsigned(ulong? a, ulong b) => a % b;

    public static long CompoundDivide(long a, long b)
    {
        var x = a;
        x /= b;

        return x;
    }

    public static long CompoundRemainder(long a, long b)
    {
        var x = a;
        x %= b;

        return x;
    }

    public static ulong CompoundDivideUnsigned(ulong a, ulong b)
    {
        var x = a;
        x /= b;

        return x;
    }

    public static long? LiftedCompoundRemainder(long? a, long b)
    {
        var x = a;
        x %= b;

        return x;
    }
}
