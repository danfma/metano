using System.Numerics;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The .NET 10 conversion of a <c>double</c> or <c>float</c> to <c>decimal</c> (<c>new
/// decimal(double)</c>, <c>(decimal)f</c>), spelled out so a constant cast folds to the same
/// digits whichever runtime hosts the compiler: Roslyn folds <c>(decimal)0.1</c> with the host's
/// own conversion, and a newer runtime converts exactly where .NET 10 rounds (#748). The input
/// is rounded to 15 significant digits for a <c>double</c> and 7 for a <c>float</c>, ties to
/// even, by the same <c>double</c> arithmetic the runtime uses, and trailing zeros of the
/// fraction are dropped.
/// </summary>
internal static class DecimalFromFloatingPoint
{
    private const int DoubleExponentBias = 1022;

    private const int MaxScale = 28;

    private const int SmallestExponent = -94;

    private const int LargestExponent = 96;

    private const int Log10Of2Scaled = 19728;

    private const int DoubleDigits = 15;

    private const int SingleDigits = 7;

    private static readonly double[] PowersOfTen =
    [
        1e0,
        1e1,
        1e2,
        1e3,
        1e4,
        1e5,
        1e6,
        1e7,
        1e8,
        1e9,
        1e10,
        1e11,
        1e12,
        1e13,
        1e14,
        1e15,
        1e16,
        1e17,
        1e18,
        1e19,
        1e20,
        1e21,
        1e22,
        1e23,
        1e24,
        1e25,
        1e26,
        1e27,
        1e28,
        1e29,
        1e30,
        1e31,
        1e32,
        1e33,
        1e34,
        1e35,
        1e36,
        1e37,
        1e38,
        1e39,
        1e40,
        1e41,
        1e42,
        1e43,
        1e44,
        1e45,
    ];

    /// <summary>
    /// The decimal a <c>double</c> or <c>float</c> constant converts to, or <c>null</c> for any
    /// other value or one the conversion rejects (NaN, an infinity, a magnitude of 2^96 or
    /// more), which C# reports as a compile error for a constant.
    /// </summary>
    public static decimal? Convert(object? value) =>
        value switch
        {
            double d => Round(d, DoubleDigits),
            float f => Round(f, SingleDigits),
            _ => null,
        };

    private static decimal? Round(double input, int digits)
    {
        if (double.IsNaN(input) || double.IsInfinity(input))
            return null;

        var negative = input < 0;
        var magnitude = Math.Abs(input);

        var exponent =
            (int)((BitConverter.DoubleToInt64Bits(magnitude) >> 52) & 0x7FF) - DoubleExponentBias;

        if (exponent < SmallestExponent)
            return 0m;

        if (exponent > LargestExponent)
            return null;

        var power = digits - 1 - ((exponent * Log10Of2Scaled) >> 16);
        var scaled = Scale(magnitude, ref power, digits);
        var mantissa = RoundToEven(scaled);

        if (mantissa == 0)
            return 0m;

        return ToDecimal(mantissa, power, digits - 1, negative);
    }

    /// <summary>
    /// Scales <paramref name="magnitude"/> to a <paramref name="digits"/>-digit number, as the
    /// runtime does: up by <c>10^power</c> (at most <c>10^28</c>), or down for a larger value,
    /// and one more digit when the result still has fewer than <paramref name="digits"/>.
    /// </summary>
    private static double Scale(double magnitude, ref int power, int digits)
    {
        var scaled = magnitude;

        if (power >= 0)
        {
            power = Math.Min(power, MaxScale);
            scaled *= PowersOfTen[power];
        }
        else if (digits == DoubleDigits && power == -1 && magnitude < 1e15)
        {
            power = 0;
        }
        else
        {
            scaled /= PowersOfTen[-power];
        }

        var lowest = PowersOfTen[digits - 1];

        if (scaled < lowest && power < MaxScale)
        {
            scaled *= 10;
            power++;
        }

        return scaled;
    }

    private static ulong RoundToEven(double scaled)
    {
        var whole = (ulong)(long)scaled;
        var fraction = scaled - (double)(long)whole;

        if (fraction > 0.5 || (fraction == 0.5 && (whole & 1) != 0))
            whole++;

        return whole;
    }

    /// <summary>
    /// <c>mantissa × 10^(−power)</c>: a negative power multiplies out to an integer, a positive
    /// one is the scale with up to <c>digits - 1</c> trailing zeros removed, the most a
    /// <c>digits</c>-digit mantissa can lose.
    /// </summary>
    private static decimal? ToDecimal(ulong mantissa, int power, int maxZerosRemoved, bool negative)
    {
        BigInteger integer = mantissa;

        if (power < 0)
            return Compose(integer * BigInteger.Pow(10, -power), 0, negative);

        var scale = power;

        for (
            var removed = 0;
            removed < maxZerosRemoved && scale > 0 && integer % 10 == 0;
            removed++
        )
        {
            integer /= 10;
            scale--;
        }

        return Compose(integer, scale, negative);
    }

    private static decimal? Compose(BigInteger integer, int scale, bool negative)
    {
        if (integer.GetBitLength() > 96)
            return null;

        var mask = new BigInteger(uint.MaxValue);
        var low = (int)(uint)(integer & mask);
        var middle = (int)(uint)((integer >> 32) & mask);
        var high = (int)(uint)((integer >> 64) & mask);

        return new decimal(low, middle, high, negative, (byte)scale);
    }
}
