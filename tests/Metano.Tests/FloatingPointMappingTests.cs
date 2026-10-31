namespace Metano.Tests;

/// <summary>
/// Declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> lowering of the
/// <c>System.Double</c> and <c>System.Single</c> helpers and special values
/// (<c>src/Metano/Runtime/FloatingPoint.cs</c>). Both types lower to the JS
/// <c>number</c>, so nothing may reach the output as an unbound
/// <c>Double.</c>/<c>Single.</c> reference.
/// </summary>
public class FloatingPointMappingTests
{
    [Test]
    [Arguments("double")]
    [Arguments("float")]
    public async Task ClassificationHelpers_LowerToNumberGlobals(string numericKeyword)
    {
        var output = TranspileNumbers(
            $"""
            public static bool IsBroken({numericKeyword} v) => {numericKeyword}.IsNaN(v) || {numericKeyword}.IsInfinity(v);
            public static bool IsUsable({numericKeyword} v) => {numericKeyword}.IsFinite(v);
            public static bool IsPositiveOverflow({numericKeyword} v) => {numericKeyword}.IsPositiveInfinity(v);
            public static bool IsNegativeOverflow({numericKeyword} v) => {numericKeyword}.IsNegativeInfinity(v);
            """
        );

        await Assert.That(output).Contains("return Number.isNaN(v) || (Math.abs(v) === Infinity);");
        await Assert.That(output).Contains("return Number.isFinite(v);");
        await Assert.That(output).Contains("return Object.is(v, Infinity);");
        await Assert.That(output).Contains("return Object.is(v, -Infinity);");
        await AssertNoBclNumericReceiverLeaks(output);
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
        await AssertNoBclNumericReceiverLeaks(output);
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
        await AssertNoBclNumericReceiverLeaks(output);
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
        await AssertNoBclNumericReceiverLeaks(output);
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
        await AssertNoBclNumericReceiverLeaks(output);
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
        await AssertNoBclNumericReceiverLeaks(output);
    }

    [Test]
    public async Task NaNConstantPatterns_LowerToNumberIsNaN()
    {
        // `x === NaN` is always false in JS, while C# `x is double.NaN` matches NaN.
        var output = TranspileNumbers(
            """
            public static bool IsMissing(double v) => v is double.NaN;
            public static bool IsPresent(float v) => v is not float.NaN;
            public static string Describe(double v) => v switch
            {
                double.NaN => "missing",
                double.PositiveInfinity => "overflow",
                _ => "value",
            };
            """
        );

        await Assert.That(output).Contains("return Number.isNaN(v);");
        await Assert.That(output).Contains("return !Number.isNaN(v);");
        await Assert
            .That(output)
            .Contains(
                "return Number.isNaN(v) ? \"missing\" : v === Infinity ? \"overflow\" : \"value\";"
            );
        await Assert.That(output).DoesNotContain("=== NaN");
        await AssertNoBclNumericReceiverLeaks(output);
    }

    [Test]
    public async Task NaNSwitchLabels_RewriteSwitchToBooleanTests()
    {
        // A JS switch compares with `===`, so `case NaN:` would never match.
        var output = TranspileNumbers(
            """
            public static string Describe(double v)
            {
                switch (v)
                {
                    case double.NaN:
                        return "missing";
                    case 0:
                    case double.PositiveInfinity:
                        return "edge";
                    default:
                        return "value";
                }
            }
            """
        );

        await Assert.That(output).Contains("switch (true)");
        await Assert.That(output).Contains("case Number.isNaN(v):");
        await Assert.That(output).Contains("case v === 0:");
        await Assert.That(output).Contains("case v === Infinity:");
        await Assert.That(output).DoesNotContain("case NaN:");
        await AssertNoBclNumericReceiverLeaks(output);
    }

    [Test]
    public async Task NaNSwitchLabels_OnACallSubject_EmitUncompilableTodo()
    {
        // Rewriting would evaluate the call once per label; emit a TODO that does not
        // compile rather than a case that silently never matches.
        var output = TranspileNumbers(
            """
            public static double Next() => 1;

            public static string Describe()
            {
                switch (Next())
                {
                    case double.NaN:
                        return "missing";
                    default:
                        return "value";
                }
            }
            """
        );

        await Assert.That(output).Contains("switch (Numbers.next())");
        await Assert
            .That(output)
            .Contains(
                "/* TODO: NaN case label needs an identifier or member-access switch subject */"
            );
        await Assert.That(output).DoesNotContain("case NaN:");
        await AssertNoBclNumericReceiverLeaks(output);
    }

    [Test]
    public async Task SwitchWithoutNaNLabels_KeepsPlainSwitch()
    {
        var output = TranspileNumbers(
            """
            public static string Describe(double v)
            {
                switch (v)
                {
                    case double.PositiveInfinity:
                        return "overflow";
                    default:
                        return "value";
                }
            }
            """
        );

        await Assert.That(output).Contains("switch (v)");
        await Assert.That(output).Contains("case Infinity:");
        await Assert.That(output).DoesNotContain("switch (true)");
        await AssertNoBclNumericReceiverLeaks(output);
    }

    [Test]
    public async Task NaNEquality_StaysStrictEquality()
    {
        // Unlike the constant pattern, `x == double.NaN` is false in .NET too.
        var output = TranspileNumbers(
            """
            public static bool EqualsNaN(double v) => v == double.NaN;
            """
        );

        await Assert.That(output).Contains("return v === NaN;");
        await AssertNoBclNumericReceiverLeaks(output);
    }

    [Test]
    public async Task SpecialValues_MapInFieldInitializersDefaultsAndUsingStatic()
    {
        // Const fields are not folded away before mapping: every position that names
        // the BCL member goes through the declarative mapping.
        var result = TranspileHelper.Transpile(
            """
            using static System.Double;

            [Transpile]
            public static class Limits
            {
                public const double Missing = double.NaN;
                public static readonly float Ceiling = float.MaxValue;
                public static double OrMissing(double v = double.NaN) => v;
                public static bool IsMissing(double v) => IsNaN(v);
            }
            """
        );

        var output = result["limits.ts"];
        await Assert.That(output).Contains("= NaN");
        await Assert.That(output).Contains("= 3.4028234663852886e+38");
        await Assert.That(output).Contains("v: number = NaN");
        await Assert.That(output).Contains("return Number.isNaN(v);");
        await AssertNoBclNumericReceiverLeaks(output);
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

    private static async Task AssertNoBclNumericReceiverLeaks(string output)
    {
        await Assert.That(output).DoesNotContain("Double.");
        await Assert.That(output).DoesNotContain("Single.");
    }
}
