namespace Metano.Tests;

/// <summary>
/// The runtime tests a `decimal` type pattern lowers to (#505). A C# `decimal`
/// is a decimal.js `Decimal` instance in the generated TypeScript, so every
/// type test (`is`, `is not`, switch arms, `as decimal?`, dispatcher guards,
/// generated guards) must test `isDecimal` (its decimal.js tag, one rule with
/// the `DecimalType` descriptor, spec 011 NFR-004) — `typeof === "number"`
/// matches plain JS numbers C# would reject and rejects every real decimal.
/// </summary>
public class DecimalTypePatternTests
{
    // Probed on .NET: boxed `1.5m` answers `is decimal` = true and
    // `is int` = false, so the two checks must stay distinguishable — exactly
    // what `isDecimal` vs `isInt32` gives.

    [Test]
    public async Task IsDecimal_TestsIsDecimal()
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

        // The user's `isDecimal` claims the name, so the runtime check is aliased.
        await Assert
            .That(output)
            .Contains("import { isDecimal as isDecimal$rt } from \"metano-runtime\";");

        await Assert.That(output).Contains("return isDecimal$rt(o);");
        await Assert.That(output).DoesNotContain("typeof o === \"number\"");
    }

    [Test]
    public async Task IsNotDecimal_NegatesTheDecimalTest()
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
        await Assert.That(output).Contains("return !isDecimal(o);");
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
        await Assert.That(output).Contains("isDecimal(o) && (d = o, true)");
        await Assert.That(output).DoesNotContain("let d: number;");
    }

    [Test]
    public async Task SwitchStatement_DecimalArm_TestsIsDecimal()
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
        // The int arm validates the value (spec 011 FR-022).
        await Assert.That(output).Contains("case isDecimal(o):");
        await Assert.That(output).Contains("case isInt32(o):");
    }

    [Test]
    public async Task SwitchExpression_DecimalArm_TestsIsDecimal()
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
            .Contains("isDecimal(o) ? \"dec\" : isFloat64(o) ? \"dbl\" : \"other\"");
    }

    [Test]
    public async Task AsDecimalNullable_ReadsTheDecimalDescriptor()
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

        await Assert.That(output).Contains("return asType(o, DecimalType) ?? new Decimal(\"0\");");

        await Assert.That(output).DoesNotContain("typeof o === \"number\"");
    }

    [Test]
    public async Task GeneratedGuard_DecimalField_TestsIsDecimal()
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
        await Assert.That(output).Contains("isDecimal(v.amount);");
        await Assert.That(output).DoesNotContain("return v.amount != null;");
    }

    [Test]
    public async Task GeneratedGuard_NullableDecimalField_TestsIsDecimalPerBranch()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            [GenerateGuard]
            public record MaybePriced(decimal? Amount);
            """
        );

        var output = result["maybe-priced.ts"];
        await Assert.That(output).Contains("v.amount == null || isDecimal(v.amount);");
    }

    [Test]
    public async Task BrandedCompanion_DecimalOverload_TestsIsDecimal()
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
        await Assert.That(output).Contains("isDecimal(args[0])");
        await Assert.That(output).DoesNotContain("typeof args[0] === \"number\"");
    }

    [Test]
    public async Task ClassDispatcher_DecimalOverload_TestsIsDecimal()
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
        await Assert.That(output).Contains("isDecimal(args[0])");
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

        await TranspileHelper.AssertGolden("decimal-type-patterns.ts", result["checker.ts"]);
    }
}
