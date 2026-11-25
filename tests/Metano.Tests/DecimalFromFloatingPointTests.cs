using Metano.Compiler.Extraction;

namespace Metano.Tests;

/// <summary>
/// #748: a constant <c>(decimal)double</c> folds by the .NET 10 conversion, spelled out in
/// <see cref="DecimalFromFloatingPoint"/>; the test host is .NET 10, so its own conversion is
/// the reference.
/// </summary>
public class DecimalFromFloatingPointTests
{
    [Test]
    [Arguments(0.1)]
    [Arguments(0.1 + 0.2)]
    [Arguments(1.0 / 3.0)]
    [Arguments(2.0 / 3.0)]
    [Arguments(123456789.123456789)]
    [Arguments(1e15)]
    [Arguments(999999999999999.9)]
    [Arguments(1e-10)]
    [Arguments(1.5e-28)]
    [Arguments(5e-29)]
    [Arguments(79228162514264337593543950335.0 / 2)]
    [Arguments(-0.1)]
    [Arguments(-0.0)]
    [Arguments(0.5)]
    [Arguments(0.1875)]
    [Arguments(4.35)]
    [Arguments(1e20)]
    [Arguments(7.0)]
    public async Task Double_MatchesTheNet10Conversion(double value)
    {
        var folded = DecimalFromFloatingPoint.Convert(value);

        await Assert.That(folded).IsEqualTo((decimal)value);
        await Assert.That(folded!.Value.ToString()).IsEqualTo(((decimal)value).ToString());
    }

    [Test]
    [Arguments(0.1f)]
    [Arguments(1.1f)]
    [Arguments(16777216f)]
    [Arguments(3.14159265f)]
    [Arguments(1e10f)]
    [Arguments(1.5e-10f)]
    [Arguments(-2.5f)]
    [Arguments(0.3f)]
    public async Task Single_MatchesTheNet10Conversion(float value)
    {
        var folded = DecimalFromFloatingPoint.Convert(value);

        await Assert.That(folded).IsEqualTo((decimal)value);
        await Assert.That(folded!.Value.ToString()).IsEqualTo(((decimal)value).ToString());
    }

    [Test]
    public async Task RandomDoubles_MatchTheNet10Conversion()
    {
        var random = new Random(748);
        var mismatches = new List<string>();

        for (var i = 0; i < 200_000; i++)
        {
            var value = RandomDouble(random);
            decimal expected;

            try
            {
                expected = (decimal)value;
            }
            catch (OverflowException)
            {
                continue;
            }

            var actual = DecimalFromFloatingPoint.Convert(value);

            if (actual != expected || actual?.ToString() != expected.ToString())
                mismatches.Add($"{value:R}: {actual} != {expected}");
        }

        await Assert.That(mismatches.Take(5)).IsEmpty();
    }

    [Test]
    public async Task RandomSingles_MatchTheNet10Conversion()
    {
        var random = new Random(749);
        var mismatches = new List<string>();

        for (var i = 0; i < 200_000; i++)
        {
            var value = (float)RandomDouble(random);

            if (float.IsInfinity(value))
                continue;

            decimal expected;

            try
            {
                expected = (decimal)value;
            }
            catch (OverflowException)
            {
                continue;
            }

            var actual = DecimalFromFloatingPoint.Convert(value);

            if (actual != expected || actual?.ToString() != expected.ToString())
                mismatches.Add($"{value:R}: {actual} != {expected}");
        }

        await Assert.That(mismatches.Take(5)).IsEmpty();
    }

    [Test]
    public async Task OtherValues_DoNotConvert()
    {
        await Assert.That(DecimalFromFloatingPoint.Convert(1)).IsNull();
        await Assert.That(DecimalFromFloatingPoint.Convert(double.NaN)).IsNull();
        await Assert.That(DecimalFromFloatingPoint.Convert(double.PositiveInfinity)).IsNull();
        await Assert.That(DecimalFromFloatingPoint.Convert(1e30)).IsNull();
    }

    private static double RandomDouble(Random random)
    {
        var mode = random.Next(3);

        if (mode == 0)
            return BitConverter.Int64BitsToDouble(random.NextInt64());

        var magnitude = Math.Pow(10, random.Next(-30, 30));

        return mode == 1
            ? random.NextDouble() * magnitude * (random.Next(2) == 0 ? 1 : -1)
            : Math.Round(random.NextDouble() * magnitude, random.Next(0, 8));
    }
}
