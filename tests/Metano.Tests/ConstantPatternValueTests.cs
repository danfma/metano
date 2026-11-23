namespace Metano.Tests;

/// <summary>
/// Constant patterns and <c>case</c> labels whose constant is named rather than
/// written as a literal: a user <c>const</c>, an enum member through
/// <c>using static</c>, a module-scope <c>const</c>, and every spelling of NaN.
/// A NaN constant is detected by value, so <c>0.0 / 0.0</c> and a <c>const</c>
/// holding NaN lower to <c>Number.isNaN</c> like <c>double.NaN</c> (#261, #262,
/// #399).
/// </summary>
public class ConstantPatternValueTests
{
    [Test]
    public async Task ArithmeticNaNPattern_LowersToNumberIsNaN()
    {
        var output = TranspileLimits("public static bool A(double v) => v is 0.0 / 0.0;");

        await Assert.That(output).Contains("return Number.isNaN(v);");
        await Assert.That(output).DoesNotContain("v === 0 / 0");
    }

    [Test]
    public async Task UserConstHoldingNaN_LowersToNumberIsNaN()
    {
        var output = TranspileLimits(
            """
            public const double MyNaN = double.NaN;
            public static bool B(double v) => v is MyNaN;
            public static bool C(double v) => v is not Limits.MyNaN;
            """
        );

        await Assert.That(output).Contains("return Number.isNaN(v);");
        await Assert.That(output).Contains("return !Number.isNaN(v);");
        await Assert.That(output).DoesNotContain("=== Limits.myNaN");
    }

    [Test]
    public async Task UsingStaticNaN_LowersToNumberIsNaN()
    {
        var result = TranspileHelper.Transpile(
            """
            using static System.Double;

            [Transpile]
            public static class Limits
            {
                public static bool C(double v) => v is NaN;
            }
            """
        );

        await Assert.That(result["limits.ts"]).Contains("return Number.isNaN(v);");
    }

    [Test]
    public async Task NaNCaseLabelThroughUserConst_RewritesSwitchToBooleanTests()
    {
        var output = TranspileLimits(
            """
            public const float Missing = float.NaN;
            public static int G(float v)
            {
                switch (v)
                {
                    case Missing:
                        return 1;
                    case 1:
                        return 2;
                    default:
                        return 0;
                }
            }
            public static int H(double v)
            {
                switch (v)
                {
                    case 0.0 / 0.0:
                        return 1;
                    default:
                        return 0;
                }
            }
            """
        );

        await Assert.That(output).Contains("case Number.isNaN(v):");
        await Assert.That(output).Contains("case v === 1:");
        await Assert.That(output).DoesNotContain("case 0 / 0:");
        await Assert.That(output).DoesNotContain("case Limits.missing:");
    }

    [Test]
    public async Task NamedUserConstants_LowerToEqualityTests()
    {
        var result = TranspileHelper.Transpile(
            """
            using static Color;

            public enum Color { Red, Green }

            [Transpile]
            public static class Limits
            {
                public const int Three = 3;
                public const string Hello = "hi";
                public static bool D(int v) => v is Three;
                public static bool E(string v) => v is Hello;
                public static bool F(Color c) => c is Green;
            }
            """
        );

        var output = result["limits.ts"];

        await Assert.That(output).Contains("return v === Limits.three;");
        await Assert.That(output).Contains("return v === Limits.hello;");
        await Assert.That(output).Contains("return c === Color.Green;");
        await Assert.That(output).DoesNotContain("TODO");
    }

    [Test]
    public async Task ModuleConst_LowersToEqualityTest()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Mod
            {
                public const int Default = 3;
            }

            [Transpile]
            public static class UsesMod
            {
                public static bool J(int x) => x is Mod.Default;
                public static bool K(object x) => x is Mod.Default;
            }
            """
        );

        var output = result["uses-mod.ts"];

        await Assert.That(output).Contains("return x === Default;");
        await Assert.That(output).DoesNotContain("typeof x");
    }

    private static string TranspileLimits(string members)
    {
        var result = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public static class Limits
            {
            {{members}}
            }
            """
        );

        return result["limits.ts"];
    }
}
