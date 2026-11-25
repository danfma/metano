namespace Metano.Tests;

/// <summary>
/// Field and property initializers that read a primary-constructor parameter
/// run in the constructor body, where the parameter is in scope; a TS field
/// initializer cannot see it (#254). Every initializer of the type moves, in
/// declaration order. A property is folded into a parameter property only
/// when it merely exposes the parameter.
/// </summary>
public class PrimaryConstructorInitializerTests
{
    [Test]
    public async Task InitializersReadingParameters_MatchTheGoldenOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Account(int amount, string owner, int bonus)
            {
                public int Amount { get; } = amount * 2;

                public string Owner => owner.ToUpper();

                private readonly int _bonus = bonus;

                private readonly int _total = bonus + 1;

                public int Twice { get; } = amount * 2;

                public int Sum() => _bonus + _total;
            }
            """
        );

        await TranspileHelper.AssertGolden("primary-ctor-initializers.ts", result["account.ts"]);
    }

    [Test]
    public async Task DerivedClassInitializer_RunsAfterTheBaseCall()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base(int value)
            {
                public int Value { get; } = value;
            }

            [Transpile]
            public class Derived(int seed) : Base(seed + 1)
            {
                public int Double { get; } = seed * 2;
            }
            """
        );

        var derived = result["derived.ts"];

        await Assert
            .That(derived)
            .Contains("super((seed + 1) | 0);\n    this.double = Math.imul(seed, 2);");

        await Assert.That(derived).DoesNotContain("private readonly _seed");
        await Assert.That(result["base.ts"]).Contains("constructor(value: number) {");
    }

    [Test]
    public async Task RecordInitializer_ReadsThePositionalParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Scaled(int Factor)
            {
                public int Double { get; } = Factor * 2;
            }
            """
        );

        await Assert
            .That(result["scaled.ts"])
            .Contains(
                "constructor(factor: number) {\n    this.factor = factor;\n    this.double = Math.imul(factor, 2);\n  }"
            );
    }

    [Test]
    public async Task LambdaInInitializer_ClosesOverTheConstructorParameter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Lazy(int seed)
            {
                private readonly System.Func<int> _next = () => seed + 1;

                public int Next() => _next();
            }
            """
        );

        var output = result["lazy.ts"];

        await Assert.That(output).Contains("this._next = () => (seed + 1) | 0;");
        await Assert.That(output).DoesNotContain("private readonly _seed");
    }

    [Test]
    public async Task LambdaInInitializer_SharesACapturedParameterWithMemberBodies()
    {
        // `Bump` mutates `seed`, so C# captures it in one slot that the
        // initializer's lambda shares: the lambda must read the backing field.
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Shared(int seed)
            {
                public readonly System.Func<int> Read = () => seed;

                public void Bump() => seed++;
            }
            """
        );

        var output = result["shared.ts"];

        await Assert.That(output).Contains("this._seed = seed;\n    this.read = () => this._seed;");
    }

    [Test]
    public async Task TypeWithoutParameterReads_KeepsItsFieldInitializers()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Tally(int start)
            {
                private int _count = 3;

                public int Next() => start + _count++;
            }
            """
        );

        await Assert.That(result["tally.ts"]).Contains("this._count = 3;");
    }
}
