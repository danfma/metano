using System.Numerics;

namespace SampleOperatorOverloading;

public readonly record struct Money(BigInteger Cents, Currency Currency)
{
    public decimal ToDecimal() => (decimal)Cents / 100m;

    public override string ToString() => $"{ToDecimal()} {Currency.ToString().ToUpper()}";

    public static Money FromCents(BigInteger cents, Currency currency) => new(cents, currency);

    public static Money FromValue(decimal amount, Currency currency) =>
        new((BigInteger)Math.Round(amount * 100m), currency);

    private static void ExpectSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new NoSameMoneyCurrencyException(left.Currency, right.Currency);
    }

    public static Money operator +(Money left, Money right)
    {
        ExpectSameCurrency(left, right);

        return left with
        {
            Cents = left.Cents + right.Cents,
        };
    }

    public static Money operator -(Money left, Money right)
    {
        ExpectSameCurrency(left, right);

        return left with
        {
            Cents = left.Cents - right.Cents,
        };
    }

    public static Money operator *(Money left, BigInteger factor)
    {
        return left with { Cents = left.Cents * factor };
    }

    public static Money operator *(Money left, Money right)
    {
        ExpectSameCurrency(left, right);

        return left with
        {
            Cents = left.Cents * right.Cents,
        };
    }

    public static Money operator /(Money left, BigInteger divisor)
    {
        return left with { Cents = left.Cents / divisor };
    }

    public static Money operator /(Money left, Money divisor)
    {
        ExpectSameCurrency(left, divisor);

        return left with
        {
            Cents = left.Cents / divisor.Cents,
        };
    }
}
