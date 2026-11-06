using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The overflow rows of the reference matrix (spec 012 FR-021–FR-026,
/// contracts/ts-emission.md § Overflow). tests/Int64Reference runs every
/// public method here over boundary operands on .NET and records the result
/// or the exception; the transpiled twin must reproduce each row.
/// </summary>
[NoContainer]
public static class Overflow
{
    private const long FnvPrime = 1099511628211L;

    public static long Add(long a, long b) => a + b;

    public static long Subtract(long a, long b) => a - b;

    public static long Multiply(long a, long b) => a * b;

    public static long Negate(long a) => -a;

    public static long Complement(long a) => ~a;

    public static long AddInt(long a, int b) => a + b;

    public static ulong AddUnsigned(ulong a, ulong b) => a + b;

    public static ulong SubtractUnsigned(ulong a, ulong b) => a - b;

    public static ulong MultiplyUnsigned(ulong a, ulong b) => a * b;

    public static ulong ComplementUnsigned(ulong a) => ~a;

    public static long AddChecked(long a, long b) => checked(a + b);

    public static long SubtractChecked(long a, long b) => checked(a - b);

    public static long MultiplyChecked(long a, long b) => checked(a * b);

    public static long NegateChecked(long a) => checked(-a);

    public static long AddIntChecked(long a, int b) => checked(a + b);

    public static ulong AddUnsignedChecked(ulong a, ulong b) => checked(a + b);

    public static ulong SubtractUnsignedChecked(ulong a, ulong b) => checked(a - b);

    public static ulong MultiplyUnsignedChecked(ulong a, ulong b) => checked(a * b);

    public static ulong ComplementUnsignedChecked(ulong a) => checked(~a);

    public static long PromotedAdd(uint a, int b) => a + b;

    public static long PromotedMultiply(uint a, int b) => a * b;

    public static long PromotedNegate(uint a) => -a;

    public static int PromotedNarrow(uint a, int b) => (int)(a + b);

    public static long? LiftedAdd(long? a, long? b) => a + b;

    public static long? LiftedMultiplyChecked(long? a, long? b) => checked(a * b);

    public static long? LiftedNegate(long? a) => -a;

    public static ulong? LiftedComplement(ulong? a) => ~a;

    public static long CheckedBlock(long a, long b)
    {
        checked
        {
            var sum = a + b;

            return sum * 2;
        }
    }

    public static long UncheckedInsideChecked(long a, long b)
    {
        checked
        {
            return unchecked(a * b) - 1;
        }
    }

    public static long CheckedLambda(long a)
    {
        checked
        {
            Func<long, long> next = x => x + 1;

            return next(a);
        }
    }

    public static long PrefixIncrement(long a)
    {
        var x = a;

        return ++x;
    }

    public static long PostfixIncrement(long a)
    {
        var x = a;

        return x++;
    }

    public static long PostfixIncrementStored(long a)
    {
        var x = a;
        x++;

        return x;
    }

    public static long PrefixDecrement(long a)
    {
        var x = a;

        return --x;
    }

    public static long PostfixDecrement(long a)
    {
        var x = a;

        return x--;
    }

    public static ulong PostfixIncrementUnsigned(ulong a)
    {
        var x = a;

        return x++;
    }

    public static ulong PrefixDecrementUnsigned(ulong a)
    {
        var x = a;

        return --x;
    }

    public static long PostfixIncrementChecked(long a)
    {
        var x = a;

        return checked(x++);
    }

    public static long PrefixDecrementChecked(long a)
    {
        var x = a;

        return checked(--x);
    }

    public static ulong PostfixDecrementUnsignedChecked(ulong a)
    {
        var x = a;

        return checked(x--);
    }

    public static long CompoundAdd(long a, long b)
    {
        var x = a;
        x += b;

        return x;
    }

    public static long CompoundSubtractInt(long a, int b)
    {
        var x = a;
        x -= b;

        return x;
    }

    public static long CompoundMultiplyChecked(long a, long b)
    {
        var x = a;
        checked
        {
            x *= b;
        }

        return x;
    }

    public static ulong CompoundAddUnsigned(ulong a, ulong b)
    {
        var x = a;
        x += b;

        return x;
    }

    public static long? LiftedIncrement(long? a)
    {
        var x = a;
        x++;

        return x;
    }

    public static long? LiftedCompoundAdd(long? a, long b)
    {
        var x = a;
        x += b;

        return x;
    }

    public static long FieldIncrement(long a) => new Counter(a).IncrementField();

    public static long PropertyCompound(long a, long b) => new Counter(a).AddToTotal(b);

    public static long ElementCompound(long a, long b) => new Counter(a).MultiplySlot(b);

    public static long ElementPostfix(long a) => new Counter(a).IncrementSlot();

    public static long ElementRemainder(long a, long b) => new Counter(a).RemainderSlot(b);

    public static long PropertyShiftLeft(long a, int n) => new Counter(a).ShiftTotal(n);

    public static long HashMix(long seed, long value) => (seed ^ value) * FnvPrime;
}
