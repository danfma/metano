namespace Metano.Tests;

public class OperatorTranspileTests
{
    [Test]
    public async Task BinaryOperator_GeneratesStaticAndInstanceHelper()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Vec2(int X, int Y)
            {
                [Name("add")]
                public static Vec2 operator +(Vec2 a, Vec2 b) =>
                    new(a.X + b.X, a.Y + b.Y);
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("operator-with-name.ts");
        await Assert.That(result["vec2.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task UnaryOperator_GeneratesStaticAndInstanceHelper()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Vec2(int X, int Y)
            {
                [Name("negate")]
                public static Vec2 operator -(Vec2 v) =>
                    new(-v.X, -v.Y);
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("unary-operator.ts");
        await Assert.That(result["vec2.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task BinaryOperator_InstanceHelper_ChainableCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Num(int Value)
            {
                [Name("add")]
                public static Num operator +(Num a, Num b) => new(a.Value + b.Value);

                [Name("multiply")]
                public static Num operator *(Num a, Num b) => new(a.Value * b.Value);
            }
            """
        );

        var output = result["num.ts"];
        // Both operators should have static + instance
        await Assert.That(output).Contains("static __add(");
        await Assert.That(output).Contains("$add(b: Num): Num");
        await Assert.That(output).Contains("static __multiply(");
        await Assert.That(output).Contains("$multiply(b: Num): Num");
    }

    [Test]
    public async Task Operator_WithoutNameAttribute_AutoDerivesName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Num(int Value)
            {
                public static Num operator +(Num a, Num b) => new(a.Value + b.Value);
            }
            """
        );

        var output = result["num.ts"];
        // Auto-derived: + → "add"
        await Assert.That(output).Contains("static __add(");
        await Assert.That(output).Contains("$add(b: Num): Num");
    }

    [Test]
    public async Task Operator_NameAttributeOverridesAutoName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Num(int Value)
            {
                [Name("plus")]
                public static Num operator +(Num a, Num b) => new(a.Value + b.Value);
            }
            """
        );

        var output = result["num.ts"];
        // [Name("plus")] overrides the default "add"
        await Assert.That(output).Contains("static __plus(");
        await Assert.That(output).Contains("$plus(b: Num): Num");
    }

    [Test]
    public async Task Operator_OnPlainClass_AutoDerivesName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public decimal Amount { get; }
                public Money(decimal amount) { Amount = amount; }

                public static Money operator +(Money a, Money b) =>
                    new(a.Amount + b.Amount);

                public static Money operator -(Money a, Money b) =>
                    new(a.Amount - b.Amount);
            }
            """
        );

        var output = result["money.ts"];
        await Assert.That(output).Contains("static __add(");
        await Assert.That(output).Contains("$add(b: Money): Money");
        await Assert.That(output).Contains("static __subtract(");
        await Assert.That(output).Contains("$subtract(b: Money): Money");
    }

    [Test]
    public async Task UnaryOperator_WithoutNameAttribute_AutoDerivesName()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public readonly record struct Vec2(int X, int Y)
            {
                public static Vec2 operator -(Vec2 v) => new(-v.X, -v.Y);
            }
            """
        );

        var output = result["vec2.ts"];
        await Assert.That(output).Contains("static __subtract(");
        await Assert.That(output).Contains("$subtract(): Vec2");
    }

    /// <summary>
    /// Operator overloads that differ in return type share one dispatcher,
    /// whose implementation signature cannot name either: it erases the
    /// return type, as a method dispatcher does, and the overload signatures
    /// keep the precise ones.
    /// </summary>
    [Test]
    public async Task OperatorOverloads_WithDifferentReturnTypes_EraseTheImplementationReturn()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Meter
            {
                public static int operator +(Meter m, int i) => i;

                public static long operator +(Meter m, long l) => l;
            }
            """
        );

        var output = result["meter.ts"];

        await Assert.That(output).Contains("static __add(m: Meter, i: number): number;");
        await Assert.That(output).Contains("static __add(m: Meter, l: bigint): bigint;");
        await Assert.That(output).Contains("static __add(...args: unknown[]): any {");
        await Assert.That(output).Contains("$add(...args: unknown[]): any {");
    }
}
