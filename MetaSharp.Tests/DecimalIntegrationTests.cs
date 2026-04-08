namespace MetaSharp.Tests;

/// <summary>
/// End-to-end tests for the C# <c>decimal</c> → <c>Decimal</c> (decimal.js) integration.
/// Covers the type-level mapping (17a), literal lowering (17b), operator lowering (17c),
/// and member-level mappings (17d).
/// </summary>
public class DecimalIntegrationTests
{
    [Test]
    public async Task DecimalField_LowersToDecimalTypeAndImports()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Price(decimal Amount);
            """
        );

        var output = result["price.ts"];
        await Assert.That(output).Contains("import { Decimal } from \"decimal.js\"");
        await Assert.That(output).Contains("amount: Decimal");
    }

    [Test]
    public async Task DecimalLiteral_LowersToNewDecimalWithStringArg()
    {
        // Decimal literals (1.5m) wrap in `new Decimal("1.5")` so decimal.js can parse
        // the exact value without going through a lossy JS number conversion.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public decimal Pi => 3.14159265358979m;
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("new Decimal(\"3.14159265358979\")");
    }

    [Test]
    public async Task DecimalArithmetic_LowersToMethodCalls()
    {
        // a + b → a.plus(b), - → minus, * → times, / → div, % → mod
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public decimal Add(decimal a, decimal b) => a + b;
                public decimal Sub(decimal a, decimal b) => a - b;
                public decimal Mul(decimal a, decimal b) => a * b;
                public decimal Div(decimal a, decimal b) => a / b;
                public decimal Mod(decimal a, decimal b) => a % b;
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return a.plus(b);");
        await Assert.That(output).Contains("return a.minus(b);");
        await Assert.That(output).Contains("return a.times(b);");
        await Assert.That(output).Contains("return a.div(b);");
        await Assert.That(output).Contains("return a.mod(b);");
    }

    [Test]
    public async Task DecimalComparison_LowersToMethodCalls()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public bool Eq(decimal a, decimal b) => a == b;
                public bool Ne(decimal a, decimal b) => a != b;
                public bool Lt(decimal a, decimal b) => a < b;
                public bool Gt(decimal a, decimal b) => a > b;
                public bool Le(decimal a, decimal b) => a <= b;
                public bool Ge(decimal a, decimal b) => a >= b;
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return a.eq(b);");
        await Assert.That(output).Contains("return !a.eq(b);");
        await Assert.That(output).Contains("return a.lt(b);");
        await Assert.That(output).Contains("return a.gt(b);");
        await Assert.That(output).Contains("return a.lte(b);");
        await Assert.That(output).Contains("return a.gte(b);");
    }

    [Test]
    public async Task DecimalNegation_LowersToNeg()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public decimal Negate(decimal x) => -x;
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return x.neg();");
    }

    [Test]
    public async Task DecimalMixedWithIntLiteral_StillLowersToMethodCall()
    {
        // The literal `2` has C# type int but ConvertedType decimal — the decimal
        // resolution should fire on the converted type so the operator becomes
        // `x.times(new Decimal("2"))` (or similar).
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public decimal Double(decimal x) => x * 2m;
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("x.times(new Decimal(\"2\"))");
    }

    [Test]
    public async Task NonDecimalLiteral_StillLowersToBareNumber()
    {
        // Sanity check: int / double / float literals are unaffected.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public int IntValue => 42;
                public double DoubleValue => 3.14;
            }
            """
        );

        var output = result["calc.ts"];
        await Assert.That(output).Contains("return 42;");
        await Assert.That(output).Contains("return 3.14;");
        await Assert.That(output).DoesNotContain("new Decimal");
    }
}
