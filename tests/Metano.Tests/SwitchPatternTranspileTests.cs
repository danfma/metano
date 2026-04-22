namespace Metano.Tests;

public class SwitchPatternTranspileTests
{
    // ─── Switch statement ───────────────────────────────────

    [Test]
    public async Task SwitchStatement_ConstantCases()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Mapper
            {
                public static string Map(int code)
                {
                    switch (code)
                    {
                        case 1: return "one";
                        case 2: return "two";
                        default: return "other";
                    }
                }
            }
            """
        );

        var output = result["mapper.ts"];
        await Assert.That(output).Contains("switch (code)");
        await Assert.That(output).Contains("case 1:");
        await Assert.That(output).Contains("case 2:");
        await Assert.That(output).Contains("default:");
    }

    // ─── Switch expression ──────────────────────────────────

    [Test]
    public async Task SwitchExpression_GeneratesTernaryChain()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Grader
            {
                public static string Grade(int score)
                {
                    return score switch
                    {
                        100 => "perfect",
                        _ => "other",
                    };
                }
            }
            """
        );

        var output = result["grader.ts"];
        await Assert.That(output).Contains("score === 100 ? \"perfect\" : \"other\"");
    }

    // ─── Is pattern ─────────────────────────────────────────

    [Test]
    public async Task IsNull_GeneratesLooseEquality()
    {
        // `is null` lowers to `== null` (loose) so the check also matches
        // a JS-side `undefined` — necessary when a TS consumer produces an
        // absent optional property or an uninitialized field. C# does not
        // expose `undefined`, so relaxing the comparison is safe for
        // round-trip semantics and matches Kotlin/JS behavior.
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, ExportedAsModule]
            public static class Checker
            {
                public static bool IsEmpty(string? value)
                {
                    return value is null;
                }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("value == null");
    }

    [Test]
    public async Task IsNotNull_GeneratesNegatedLooseEquality()
    {
        // `is not null` lowers to `!(value == null)` so the negation also
        // excludes `undefined`. Pairs with `IsNull_GeneratesLooseEquality`
        // — both sides of the C# pattern collapse to the same loose
        // comparison on the TS side.
        var result = TranspileHelper.Transpile(
            """
            #nullable enable
            [Transpile, ExportedAsModule]
            public static class Checker
            {
                public static bool HasValue(string? value)
                {
                    return value is not null;
                }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("!(value == null)");
    }

    [Test]
    public async Task IsConstant_GeneratesEquality()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Checker
            {
                public static bool IsZero(int x)
                {
                    return x is 0;
                }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("x === 0");
    }

    [Test]
    public async Task IsType_GeneratesInstanceof()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile]
                public record Money(int Cents);

                [Transpile, ExportedAsModule]
                public static class Checker
                {
                    public static bool IsMoney(object value)
                    {
                        return value is Money;
                    }
                }
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("value instanceof Money");
    }

    [Test]
    public async Task IsRelational_GeneratesComparison()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Validator
            {
                public static bool IsPositive(int x)
                {
                    return x is > 0;
                }
            }
            """
        );

        var output = result["validator.ts"];
        await Assert.That(output).Contains("x > 0");
    }

    [Test]
    public async Task IsCombinedPattern_GeneratesLogicalAnd()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Range
            {
                public static bool InRange(int x)
                {
                    return x is >= 0 and < 100;
                }
            }
            """
        );

        var output = result["range.ts"];
        await Assert.That(output).Contains("x >= 0 && x < 100");
    }

    [Test]
    public async Task IsOrPattern_GeneratesLogicalOr()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class StatusCheck
            {
                public static bool IsSpecial(int x)
                {
                    return x is 0 or 1;
                }
            }
            """
        );

        var output = result["status-check.ts"];
        await Assert.That(output).Contains("x === 0 || x === 1");
    }

    // ─── Switch expression with patterns ────────────────────

    [Test]
    public async Task SwitchExpression_WithRelationalPatterns()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Grader
            {
                public static string Classify(int score)
                {
                    return score switch
                    {
                        >= 90 => "A",
                        >= 80 => "B",
                        _ => "F",
                    };
                }
            }
            """
        );

        var output = result["grader.ts"];
        await Assert.That(output).Contains("score >= 90 ? \"A\"");
        await Assert.That(output).Contains("score >= 80 ? \"B\"");
        await Assert.That(output).Contains("\"F\"");
    }
}
