namespace Metano.Tests;

/// <summary>
/// Lowering of <c>Math.Round</c> / <c>decimal.Round</c> (<c>src/Metano/Runtime/Math.cs</c>,
/// <c>Decimal.cs</c>) onto the half-to-even runtime helpers. JS <c>Math.round</c> takes no
/// digit count and rounds a midpoint toward +Infinity, so no overload may reach the output
/// as <c>Math.round</c>; the <c>(value, mode)</c> overloads are normalized to
/// <c>(value, 0, mode)</c> by the extractor.
/// </summary>
public class RoundingMappingTests
{
    [Test]
    public async Task DoubleRoundOverloads_LowerToRoundNumber()
    {
        var output = TranspileNumbers(
            """
            public static double Whole(double x) => Math.Round(x);
            public static double Cents(double x) => Math.Round(x, 2);
            public static double Away(double x) => Math.Round(x, MidpointRounding.AwayFromZero);
            public static double Mode(double x, MidpointRounding mode) => Math.Round(x, 3, mode);
            """
        );

        await Assert.That(output).Contains("return roundNumber(x);");
        await Assert.That(output).Contains("return roundNumber(x, 2);");
        await Assert
            .That(output)
            .Contains("return roundNumber(x, 0, MidpointRounding.AwayFromZero);");
        await Assert.That(output).Contains("static mode(x: number, mode: MidpointRounding)");
        await Assert.That(output).Contains("return roundNumber(x, 3, mode);");
        await Assert
            .That(output)
            .Contains("import { MidpointRounding, roundNumber } from \"metano-runtime\";");
        await Assert.That(output).DoesNotContain("Math.round");
    }

    [Test]
    public async Task NamedRoundArguments_KeepParameterOrder()
    {
        var output = TranspileNumbers(
            """
            public static double Toward(double x) => Math.Round(mode: MidpointRounding.ToZero, value: x);
            """
        );

        await Assert.That(output).Contains("return roundNumber(x, 0, MidpointRounding.ToZero);");
    }

    [Test]
    public async Task DecimalRoundOverloads_LowerToRoundDecimal()
    {
        var output = TranspileNumbers(
            """
            public static decimal Whole(decimal x) => Math.Round(x);
            public static decimal Cents(decimal x) => Math.Round(x, 2);
            public static decimal Away(decimal x) => Math.Round(x, MidpointRounding.AwayFromZero);
            public static decimal Direct(decimal x) => decimal.Round(x, MidpointRounding.ToZero);
            public static decimal Full(decimal x) => decimal.Round(x, 1, MidpointRounding.ToEven);
            """
        );

        await Assert.That(output).Contains("return roundDecimal(x);");
        await Assert.That(output).Contains("return roundDecimal(x, 2);");
        await Assert
            .That(output)
            .Contains("return roundDecimal(x, 0, MidpointRounding.AwayFromZero);");
        await Assert.That(output).Contains("return roundDecimal(x, 0, MidpointRounding.ToZero);");
        await Assert.That(output).Contains("return roundDecimal(x, 1, MidpointRounding.ToEven);");
        await Assert.That(output).DoesNotContain(".round()");
        await Assert.That(output).DoesNotContain("Math.round");
    }

    [Test]
    public async Task Truncate_LowersToTrunc()
    {
        var output = TranspileNumbers(
            """
            public static double Whole(double x) => Math.Truncate(x);
            public static decimal WholeDecimal(decimal x) => Math.Truncate(x);
            """
        );

        await Assert.That(output).Contains("return Math.trunc(x);");
        await Assert.That(output).Contains("return x.trunc();");
    }

    [Test]
    public async Task RoundingLowering_MatchesGolden()
    {
        var output = TranspileNumbers(GoldenSource);

        await Assert.That(output).IsEqualTo(TranspileHelper.ReadExpected("rounding.ts"));
    }

    private const string GoldenSource = """
        public static double Whole(double x) => Math.Round(x);
        public static double Cents(double? x, double fallback) => Math.Round(x ?? fallback, 2);
        public static double Away(double x) => Math.Round(x, MidpointRounding.AwayFromZero);
        public static double Mode(double x, int digits, MidpointRounding mode) => Math.Round(x, digits, mode);
        public static double Truncated(double x) => Math.Truncate(x);
        public static decimal Price(decimal amount) => Math.Round(amount * 1.1m, 2);
        public static decimal Even(decimal amount) => decimal.Round(amount, MidpointRounding.ToEven);
        """;

    private static string TranspileNumbers(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public static class Numbers
            {
            {{members}}
            }
            """
        );

        return result["numbers.ts"];
    }
}
