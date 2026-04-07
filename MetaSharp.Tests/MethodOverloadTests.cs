namespace MetaSharp.Tests;

public class MethodOverloadTests
{
    [Test]
    public async Task SingleMethod_NoDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Calculator
            {
                public int Add(int a, int b) { return a + b; }
            }
            """
        );

        var output = result["calculator.ts"];
        await Assert.That(output).DoesNotContain("...args");
        await Assert.That(output).Contains("add(a: number, b: number): number");
    }

    [Test]
    public async Task TwoOverloads_GeneratesDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Formatter
            {
                public string Format(int value) { return value.ToString(); }
                public string Format(string text) { return text; }
            }
            """
        );

        var output = result["formatter.ts"];
        // Overload signatures
        await Assert.That(output).Contains("format(value: number): string;");
        await Assert.That(output).Contains("format(text: string): string;");
        // Dispatcher
        await Assert.That(output).Contains("...args: unknown[]");
        // Type checks
        await Assert.That(output).Contains("isInt32");
        await Assert.That(output).Contains("isString");
    }

    [Test]
    public async Task StaticOverloads_GeneratesStaticDispatcher()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class MathHelper
            {
                public static int Max(int a, int b) { return a > b ? a : b; }
                public static int Max(int a, int b, int c) { return Max(Max(a, b), c); }
            }
            """
        );

        var output = result["math-helper.ts"];
        // Overload signatures should be static
        await Assert.That(output).Contains("static max(a: number, b: number, c: number): number;");
        await Assert.That(output).Contains("static max(a: number, b: number): number;");
        // Dispatcher should be static
        await Assert.That(output).Contains("static max(...args: unknown[]): number");
    }

    [Test]
    public async Task VoidOverloads_HandlesVoidReturn()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Logger
            {
                public void Log(string message) { Console.WriteLine(message); }
                public void Log(string message, int level) { Console.WriteLine($"{level}: {message}"); }
            }
            """
        );

        var output = result["logger.ts"];
        // Overload signatures
        await Assert.That(output).Contains("log(message: string, level: number): void;");
        await Assert.That(output).Contains("log(message: string): void;");
        // Dispatcher
        await Assert.That(output).Contains("...args: unknown[]");
        // Should not have throw for matching overload (has bare return instead)
        await Assert.That(output).Contains("No matching overload for log");
    }

    [Test]
    public async Task DifferentReturnTypes_UsesCommonReturn()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Parser
            {
                public int Parse(int value) { return value; }
                public string Parse(string text) { return text; }
            }
            """
        );

        var output = result["parser.ts"];
        // Individual overload signatures keep their own return types
        await Assert.That(output).Contains("parse(value: number): number;");
        await Assert.That(output).Contains("parse(text: string): string;");
        // Dispatcher should NOT have a specific type (uses unknown or any since they differ)
        await Assert.That(output).DoesNotContain("...args: unknown[]): number {");
        await Assert.That(output).DoesNotContain("...args: unknown[]): string {");
    }

    [Test]
    public async Task MixedPrimitiveAndClass_UsesCorrectChecks()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Point
            {
                public int X { get; }
                public int Y { get; }
                public Point(int x, int y) { X = x; Y = y; }
            }

            [Transpile]
            public class Geometry
            {
                public double Distance(int x, int y) { return 0; }
                public double Distance(Point p) { return 0; }
            }
            """
        );

        var output = result["geometry.ts"];
        // Overload signatures
        await Assert.That(output).Contains("distance(x: number, y: number): number;");
        await Assert.That(output).Contains("distance(p: Point): number;");
        // Type checks: primitives use isInt32, class uses instanceof
        await Assert.That(output).Contains("isInt32");
        await Assert.That(output).Contains("instanceof Point");
    }

    [Test]
    public async Task ThreeOverloads_AllSignaturesGenerated()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Builder
            {
                public string Build(string name, int count, bool flag) { return name; }
                public string Build(string name, int count) { return name; }
                public string Build(string name) { return name; }
            }
            """
        );

        var output = result["builder.ts"];
        // All three overload signatures
        await Assert.That(output).Contains("build(name: string, count: number, flag: boolean): string;");
        await Assert.That(output).Contains("build(name: string, count: number): string;");
        await Assert.That(output).Contains("build(name: string): string;");
        // Dispatcher
        await Assert.That(output).Contains("...args: unknown[]");
        // Type checks
        await Assert.That(output).Contains("isString");
        await Assert.That(output).Contains("isInt32");
        await Assert.That(output).Contains("isBool");
    }

    [Test]
    public async Task AsyncOverloads_DispatcherIsAsync()
    {
        var result = TranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public class Repository
            {
                public async Task<string> FindAsync(int id) { return await Task.FromResult(id.ToString()); }
                public async Task<string> FindAsync(string name) { return await Task.FromResult(name); }
            }
            """
        );

        var output = result["repository.ts"];
        // Dispatcher should be async
        await Assert.That(output).Contains("async findAsync(...args: unknown[])");
    }
}
