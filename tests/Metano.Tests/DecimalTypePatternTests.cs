namespace Metano.Tests;

/// <summary>
/// The runtime tests a `decimal` type pattern lowers to (#505). A C# `decimal`
/// is a decimal.js `Decimal` instance in the generated TypeScript, so every
/// type test (`is`, `is not`, switch arms, `as decimal?`, dispatcher guards,
/// generated guards) must test `instanceof Decimal` — `typeof === "number"`
/// matches plain JS numbers C# would reject and rejects every real decimal.
/// </summary>
public class DecimalTypePatternTests
{
    // Probed on .NET: boxed `1.5m` answers `is decimal` = true and
    // `is int` = false, so the two checks must stay distinguishable — exactly
    // what `instanceof` vs `typeof "number"` gives.

    [Test]
    public async Task IsDecimal_TestsInstanceofDecimal()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Checker
            {
                public static bool IsDecimal(object o) => o is decimal;
            }
            """
        );

        var output = result["checker.ts"];

        await Assert.That(output).Contains("import { Decimal } from \"decimal.js\";");
        await Assert.That(output).Contains("return o instanceof Decimal;");
        await Assert.That(output).DoesNotContain("typeof o === \"number\"");
    }

    [Test]
    public async Task IsNotDecimal_NegatesTheInstanceofTest()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Checker
            {
                public static bool IsNotDecimal(object o) => o is not decimal;
            }
            """
        );

        var output = result["checker.ts"];
        await Assert.That(output).Contains("return !(o instanceof Decimal);");
        await Assert.That(output).DoesNotContain("typeof o === \"number\"");
    }

    [Test]
    public async Task IsDecimal_Designator_BindsAndTypesTheLocal()
    {
        // A container class keeps the [ExportFromBcl] overrides in scope, so
        // the designator's declared `let` carries the Decimal type the test
        // narrows the subject to.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Checker
            {
                public static bool IsPositive(object o) => o is decimal d && d > 0;
            }
            """
        );

        var output = result["checker.ts"];

        await Assert.That(output).Contains("let d: Decimal;");
        await Assert.That(output).Contains("o instanceof Decimal && (d = o, true)");
        await Assert.That(output).DoesNotContain("let d: number;");
    }

    [Test]
    public async Task SwitchStatement_DecimalArm_TestsInstanceofDecimal()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Classifier
            {
                public static string Of(object o)
                {
                    switch (o)
                    {
                        case decimal d: return "dec";
                        case int i: return "int";
                        default: return "other";
                    }
                }
            }
            """
        );

        var output = result["classifier.ts"];

        // A decimal and an int arm can no longer share one `typeof` test —
        // the decimal arm runs first, as a boxed decimal is not an int in C#.
        await Assert.That(output).Contains("case o instanceof Decimal:");
        await Assert.That(output.Split("typeof o === \"number\"").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task SwitchExpression_DecimalArm_TestsInstanceofDecimal()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Classifier
            {
                public static string Of(object o) => o switch
                {
                    decimal => "dec",
                    double => "dbl",
                    _ => "other",
                };
            }
            """
        );

        var output = result["classifier.ts"];
        await Assert
            .That(output)
            .Contains(
                "o instanceof Decimal ? \"dec\" : typeof o === \"number\" ? \"dbl\" : \"other\""
            );
    }

    [Test]
    public async Task AsDecimalNullable_NarrowsWithInstanceof()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Defaults
            {
                public static decimal OrZero(object o) => o as decimal? ?? 0m;
            }
            """
        );

        var output = result["defaults.ts"];
        await Assert
            .That(output)
            .Contains("return (o instanceof Decimal ? o : null) ?? new Decimal(\"0\");");
        await Assert.That(output).DoesNotContain("typeof o === \"number\"");
    }

    [Test]
    public async Task GeneratedGuard_DecimalField_TestsInstanceofDecimal()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [GenerateGuard]
            public record Priced(decimal Amount);
            """
        );

        var output = result["priced.ts"];

        // The shape check must reject a plain number and accept a Decimal —
        // a bare `!= null` accepts both.
        await Assert.That(output).Contains("v.amount instanceof Decimal;");
        await Assert.That(output).DoesNotContain("return v.amount != null;");
    }

    [Test]
    public async Task GeneratedGuard_NullableDecimalField_TestsInstanceofPerBranch()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [GenerateGuard]
            public record MaybePriced(decimal? Amount);
            """
        );

        var output = result["maybe-priced.ts"];
        await Assert.That(output).Contains("v.amount == null || v.amount instanceof Decimal;");
    }

    [Test]
    public async Task BrandedCompanion_DecimalOverload_TestsInstanceofDecimal()
    {
        // Companion-namespace dispatchers build their guards without the
        // class emitter's [ExportFromBcl] overrides, so the decimal check
        // must not depend on them.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [InlineWrapper]
            public readonly struct Amount
            {
                public decimal Value { get; }

                public Amount(decimal value) => Value = value;

                public static implicit operator decimal(Amount self) => self.Value;

                public static int Pick(int x) => 1;
                public static int Pick(decimal x) => 2;
            }
            """
        );

        var output = result["amount.ts"];
        await Assert.That(output).Contains("args[0] instanceof Decimal");
        await Assert.That(output).DoesNotContain("typeof args[0] === \"number\"");
    }

    [Test]
    public async Task ClassDispatcher_DecimalOverload_KeepsInstanceofDecimal()
    {
        // The class emitter resolves decimal through the [ExportFromBcl]
        // overrides already; pin that the dispatcher guard says instanceof.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Classifier
            {
                public static int Pick(int x) => 1;
                public static int Pick(decimal x) => 2;
            }
            """
        );

        var output = result["classifier.ts"];
        await Assert.That(output).Contains("args[0] instanceof Decimal");
        await Assert.That(output).DoesNotContain("isFloat64");
    }

    [Test]
    public async Task DecimalTypePatterns_PinFullOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Checker
            {
                public static bool IsDecimal(object o) => o is decimal;
                public static bool IsNotDecimal(object o) => o is not decimal;
                public static bool IsPositive(object o) => o is decimal d && d > 0;
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("decimal-type-patterns.ts");
        await Assert.That(result["checker.ts"]).IsEqualTo(expected);
    }
}
