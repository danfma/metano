namespace Metano.Tests;

/// <summary>
/// A relational pattern over a reference input (<c>object? o; o is &lt; 3</c>)
/// matches only a value of the constant's type (#323), so it lowers to that
/// type test and the comparison: a raw <c>o &lt; 3</c> is true for
/// <c>null</c> in JS and does not type-check.
/// </summary>
public class ReferenceInputRelationalPatternTests
{
    [Test]
    public async Task IsRelational_OverObject_TestsTheTypeBeforeComparing()
    {
        var output = Transpile(
            """
            public static bool Small(object? o) => o is < 3;
            public static bool Range(object? o) => o is > 1 and < 5;
            """
        );

        await Assert.That(output).Contains("return typeof o === \"number\" && o < 3;");
        await Assert.That(output).Contains("return typeof o === \"number\" && o > 1 && o < 5;");
    }

    [Test]
    public async Task SwitchRelationalLabel_OverObject_TestsTheTypeBeforeComparing()
    {
        var output = Transpile(
            """
            public static string Describe(object? o)
            {
                switch (o)
                {
                    case < 3:
                        return "small";
                    default:
                        return "other";
                }
            }
            """
        );

        await Assert.That(output).Contains("typeof o === \"number\" && o < 3");
    }

    [Test]
    public async Task IsRelational_OverNullableValue_KeepsTheNullTest()
    {
        var output = Transpile("public static bool Positive(int? n) => n is > 0;");

        await Assert.That(output).Contains("return !(n == null) && n > 0;");
    }

    [Test]
    public async Task IsRelational_OverNonNullableValue_StaysAComparison()
    {
        var output = Transpile("public static bool Positive(int n) => n is > 0;");

        await Assert.That(output).Contains("return n > 0;");
    }

    private static string Transpile(string members) =>
        TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using Metano.Annotations;

            [Transpile]
            public static class Patterns
            {
                {{members}}
            }
            """
        )["patterns.ts"];
}
