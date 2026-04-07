namespace MetaSharp.Tests;

public class ConstructorOverloadTests
{
    [Test]
    public async Task SingleConstructor_NoDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Simple
            {
                public int X { get; }
                public Simple(int x) { X = x; }
            }
            """
        );

        var output = result["simple.ts"];
        // Single constructor should NOT have overload signatures or ...args
        await Assert.That(output).DoesNotContain("...args");
        await Assert.That(output).Contains("constructor(");
    }

    [Test]
    public async Task TwoConstructors_GeneratesDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Point
            {
                public int X { get; }
                public int Y { get; }

                public Point(int x, int y) { X = x; Y = y; }
                public Point() { X = 0; Y = 0; }
            }
            """
        );

        var output = result["point.ts"];
        // Should have overload signatures
        await Assert.That(output).Contains("constructor(public x: number, public y: number);");
        await Assert.That(output).Contains("constructor();");
        // Should have dispatcher
        await Assert.That(output).Contains("...args: unknown[]");
        // Should have type checks
        await Assert.That(output).Contains("isInt32");
    }

    [Test]
    public async Task CopyConstructor_UsesInstanceof()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Vec
            {
                public int X { get; }

                public Vec(int x) { X = x; }
                public Vec(Vec other) { X = other.X; }
            }
            """
        );

        var output = result["vec.ts"];
        await Assert.That(output).Contains("instanceof Vec");
    }

    [Test]
    public async Task DifferentPrimitiveTypes_UsesSpecializedChecks()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Converter
            {
                public string Value { get; }

                public Converter(int num) { Value = num.ToString(); }
                public Converter(string text) { Value = text; }
            }
            """
        );

        var output = result["converter.ts"];
        await Assert.That(output).Contains("isInt32");
        await Assert.That(output).Contains("isString");
    }

    [Test]
    public async Task ThreeConstructors_AllOverloadsGenerated()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Config
            {
                public string Name { get; }
                public int Value { get; }

                public Config(string name, int value) { Name = name; Value = value; }
                public Config(string name) { Name = name; Value = 0; }
                public Config() { Name = "default"; Value = 0; }
            }
            """
        );

        var output = result["config.ts"];
        // Three overload signatures
        await Assert.That(output).Contains("constructor(public name: string, public value: number);");
        await Assert.That(output).Contains("constructor(public name: string);");
        await Assert.That(output).Contains("constructor();");
        await Assert.That(output).Contains("...args: unknown[]");
    }
}
