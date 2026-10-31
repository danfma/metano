namespace Metano.Tests;

/// <summary>
/// Declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> lowering of the
/// <c>System.Double</c> and <c>System.Single</c> helpers and special values
/// (<c>src/Metano/Runtime/Numbers.cs</c>). Both types lower to the JS
/// <c>number</c>, so nothing may reach the output as an unbound
/// <c>Double.</c>/<c>Single.</c> reference.
/// </summary>
public class FloatingPointMappingTests
{
    [Test]
    public async Task DoubleClassificationHelpers_LowerToNumberGlobals()
    {
        var output = TranspileNumbers(
            """
            public static bool IsBroken(double v) => double.IsNaN(v) || double.IsInfinity(v);
            public static bool IsUsable(double v) => double.IsFinite(v);
            public static bool IsPositiveOverflow(double v) => double.IsPositiveInfinity(v);
            public static bool IsNegativeOverflow(double v) => double.IsNegativeInfinity(v);
            """
        );

        await Assert.That(output).Contains("return Number.isNaN(v) || (Math.abs(v) === Infinity);");
        await Assert.That(output).Contains("return Number.isFinite(v);");
        await Assert.That(output).Contains("return Object.is(v, Infinity);");
        await Assert.That(output).Contains("return Object.is(v, -Infinity);");
        await AssertNoUnboundNumericTypes(output);
    }

    [Test]
    public async Task SingleClassificationHelpers_LowerToNumberGlobals()
    {
        var output = TranspileNumbers(
            """
            public static bool IsBroken(float v) => float.IsNaN(v) || float.IsInfinity(v);
            public static bool IsUsable(float v) => float.IsFinite(v);
            public static bool IsPositiveOverflow(float v) => float.IsPositiveInfinity(v);
            public static bool IsNegativeOverflow(float v) => float.IsNegativeInfinity(v);
            """
        );

        await Assert.That(output).Contains("return Number.isNaN(v) || (Math.abs(v) === Infinity);");
        await Assert.That(output).Contains("return Number.isFinite(v);");
        await Assert.That(output).Contains("return Object.is(v, Infinity);");
        await Assert.That(output).Contains("return Object.is(v, -Infinity);");
        await AssertNoUnboundNumericTypes(output);
    }

    [Test]
    public async Task ClassificationHelpers_KeepCompositeArgumentsGrouped()
    {
        // Templates are substituted as text: the argument must stay a call argument
        // (or the template must parenthesize itself) so `a ?? b` and `!` keep their
        // C# meaning.
        var output = TranspileNumbers(
            """
            public static bool IsFallbackInfinite(double? a, double b) => double.IsInfinity(a ?? b);
            public static bool IsNotOverflow(double a, double b) => !double.IsPositiveInfinity(a - b);
            public static bool IsNotInfinite(double v) => !double.IsInfinity(v);
            """
        );

        await Assert.That(output).Contains("return (Math.abs(a ?? b) === Infinity);");
        await Assert.That(output).Contains("return !Object.is(a - b, Infinity);");
        await Assert.That(output).Contains("return !(Math.abs(v) === Infinity);");
    }

    [Test]
    public async Task DoubleSpecialValues_LowerToJavaScriptConstants()
    {
        var output = TranspileNumbers(
            """
            public static double NotANumber() => double.NaN;
            public static double Overflow() => double.PositiveInfinity;
            public static double Underflow() => double.NegativeInfinity;
            public static double Smallest() => double.Epsilon;
            public static double Largest() => double.MaxValue;
            public static double Lowest() => double.MinValue;
            """
        );

        await Assert.That(output).Contains("return NaN;");
        await Assert.That(output).Contains("return Infinity;");
        await Assert.That(output).Contains("return Number.NEGATIVE_INFINITY;");
        await Assert.That(output).Contains("return Number.MIN_VALUE;");
        await Assert.That(output).Contains("return Number.MAX_VALUE;");
        await Assert.That(output).Contains("return (-Number.MAX_VALUE);");
        await AssertNoUnboundNumericTypes(output);
    }

    [Test]
    public async Task SingleSpecialValues_LowerToExactFloatValues()
    {
        // float.MaxValue/Epsilon differ from the double ones; the templates carry the
        // exact double value of each float so `Math.fround` round-trips them.
        var output = TranspileNumbers(
            """
            public static float NotANumber() => float.NaN;
            public static float Overflow() => float.PositiveInfinity;
            public static float Underflow() => float.NegativeInfinity;
            public static float Smallest() => float.Epsilon;
            public static float Largest() => float.MaxValue;
            public static float Lowest() => float.MinValue;
            """
        );

        await Assert.That(output).Contains("return NaN;");
        await Assert.That(output).Contains("return Infinity;");
        await Assert.That(output).Contains("return Number.NEGATIVE_INFINITY;");
        await Assert.That(output).Contains("return 1.401298464324817e-45;");
        await Assert.That(output).Contains("return 3.4028234663852886e+38;");
        await Assert.That(output).Contains("return (-3.4028234663852886e+38);");
        await AssertNoUnboundNumericTypes(output);
    }

    [Test]
    public async Task NegativeSpecialValues_SurviveUnaryAndBinaryMinus()
    {
        // A bare `-Infinity` template would print `--Infinity` (a decrement) under a
        // unary minus at the call site.
        var output = TranspileNumbers(
            """
            public static double Negated() => -double.NegativeInfinity;
            public static double NegatedLowest() => -double.MinValue;
            public static double Distance(double x) => x - double.NegativeInfinity;
            """
        );

        await Assert.That(output).Contains("return -Number.NEGATIVE_INFINITY;");
        await Assert.That(output).Contains("return -(-Number.MAX_VALUE);");
        await Assert.That(output).Contains("return x - Number.NEGATIVE_INFINITY;");
    }

    [Test]
    public async Task ClassificationHelpers_ApplyToBrandedDoublesThroughImplicitConversion()
    {
        var result = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, Branded]
            public readonly record struct Meters(double Value)
            {
                public static implicit operator double(Meters m) => m.Value;
            }

            [Transpile]
            public static class Distances
            {
                public static bool IsUnknown(Meters m) => double.IsNaN(m);
            }
            """
        );

        var output = result["distances.ts"];
        await Assert.That(output).Contains("return Number.isNaN(m);");
        await AssertNoUnboundNumericTypes(output);
    }

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

    private static async Task AssertNoUnboundNumericTypes(string output)
    {
        await Assert.That(output).DoesNotContain("Double.");
        await Assert.That(output).DoesNotContain("Single.");
    }
}
