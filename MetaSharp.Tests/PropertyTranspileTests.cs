namespace MetaSharp.Tests;

public class PropertyTranspileTests
{
    [Test]
    public async Task ComputedProperty_ExpressionBodied_GeneratesGetter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Rect(int Width, int Height)
            {
                public int Area => Width * Height;
            }
            """
        );

        var output = result["rect.ts"];
        await Assert.That(output).Contains("get area(): number");
        await Assert.That(output).Contains("this.width * this.height");
    }

    [Test]
    public async Task ComputedProperty_GetterBlock_GeneratesGetter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Value(int X)
            {
                public int Abs
                {
                    get { return X < 0 ? -X : X; }
                }
            }
            """
        );

        var output = result["value.ts"];
        await Assert.That(output).Contains("get abs(): number");
        await Assert.That(output).Contains("this.x < 0");
    }

    [Test]
    public async Task PropertyWithGetterAndSetter()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Counter
            {
                private int _count;

                public int Count
                {
                    get { return _count; }
                    set { _count = value; }
                }
            }
            """
        );

        var output = result["counter.ts"];
        await Assert.That(output).Contains("get count(): number");
        await Assert.That(output).Contains("set count(value: number)");
    }

    [Test]
    public async Task AutoProperty_NonConstructor_GeneratesField()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Item(string Name)
            {
                public int Quantity { get; set; }
            }
            """
        );

        var output = result["item.ts"];
        await Assert.That(output).Contains("quantity: number");
        // Should not be in constructor params
        await Assert.That(output).DoesNotContain("constructor(readonly name: string, ");
    }

    [Test]
    public async Task AutoProperty_WithInitializer_GeneratesFieldWithDefault()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Config(string Name)
            {
                public int Retries { get; set; } = 3;
            }
            """
        );

        var output = result["config.ts"];
        await Assert.That(output).Contains("retries: number = 3");
    }

    [Test]
    public async Task ReadonlyAutoProperty_NonConstructor()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Point(int X, int Y)
            {
                public int Sum { get; } = 0;
            }
            """
        );

        var output = result["point.ts"];
        await Assert.That(output).Contains("readonly sum: number");
    }

    [Test]
    public async Task PrivateSetter_GeneratesPrivateSet()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class State
            {
                public string Status { get; private set; } = "idle";
            }
            """
        );

        var output = result["state.ts"];
        // Auto-property with private setter → field (not getter/setter pair)
        await Assert.That(output).Contains("status: string");
    }
}
