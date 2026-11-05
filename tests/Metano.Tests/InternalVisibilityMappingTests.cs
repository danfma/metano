namespace Metano.Tests;

/// <summary>
/// Issue #445: one shared visibility table for every TypeScript emission site.
/// C# <c>internal</c> and <c>protected internal</c> widen to public TypeScript
/// members tagged <c>/** @internal *&#47;</c> — same-assembly callers reach
/// them from any class, so the emitted surface must not restrict them — while
/// <c>private</c> stays <c>private</c> and <c>protected</c> stays
/// <c>protected</c>.
/// </summary>
public class InternalVisibilityMappingTests
{
    private const string WidgetSource = """
        namespace App;

        [Transpile]
        public class Widget
        {
            {ACCESSIBILITY} int Count;

            {ACCESSIBILITY} int Steps { get; set; }

            {ACCESSIBILITY} void Reset() { }
        }
        """;

    [Test]
    [Arguments("public", "  count: number = 0;", "  steps: number = 0;", "  reset(): void")]
    [Arguments(
        "protected",
        "  protected count: number = 0;",
        "  protected steps: number = 0;",
        "  protected reset(): void"
    )]
    [Arguments(
        "internal",
        "  /** @internal */\n  count: number = 0;",
        "  /** @internal */\n  steps: number = 0;",
        "  /** @internal */\n  reset(): void"
    )]
    [Arguments(
        "protected internal",
        "  /** @internal */\n  count: number = 0;",
        "  /** @internal */\n  steps: number = 0;",
        "  /** @internal */\n  reset(): void"
    )]
    [Arguments(
        "private",
        "  private count: number = 0;",
        "  private steps: number = 0;",
        "  private reset(): void"
    )]
    public async Task FieldPropertyAndMethod_MapThroughTheSharedTable(
        string accessibility,
        string fieldLine,
        string propertyLine,
        string methodLine
    )
    {
        var files = TranspileHelper.Transpile(
            WidgetSource.Replace("{ACCESSIBILITY}", accessibility)
        );
        var output = files["app/widget.ts"];

        await Assert.That(output).Contains(fieldLine + "\n");
        await Assert.That(output).Contains(propertyLine + "\n");
        await Assert.That(output).Contains(methodLine);
    }

    [Test]
    [Arguments("protected", "  protected static value: number = 0;")]
    [Arguments("internal", "  /** @internal */\n  static value: number = 0;")]
    [Arguments("protected internal", "  /** @internal */\n  static value: number = 0;")]
    [Arguments("private", "  private static value: number = 0;")]
    public async Task StaticField_MapsThroughTheSharedTable(string accessibility, string fieldLine)
    {
        var files = TranspileHelper.Transpile(
            $$"""
            namespace App;

            [Transpile]
            public class Widget
            {
                {{accessibility}} static int Value;

                public int Read() => Value;
            }
            """
        );

        await Assert.That(files["app/widget.ts"]).Contains(fieldLine + "\n");
    }

    [Test]
    [Arguments(
        "internal",
        "  /** @internal */\n  add(a: number, b: number): number;\n  /** @internal */\n  add(text: string): number;",
        "  add(...args: unknown[]): number {",
        "  private addAB(a: number, b: number): number {",
        "  private addText(text: string): number {"
    )]
    [Arguments(
        "protected internal",
        "  /** @internal */\n  add(a: number, b: number): number;\n  /** @internal */\n  add(text: string): number;",
        "  add(...args: unknown[]): number {",
        "  private addAB(a: number, b: number): number {",
        "  private addText(text: string): number {"
    )]
    [Arguments(
        "protected",
        "  protected add(a: number, b: number): number;\n  protected add(text: string): number;",
        "  protected add(...args: unknown[]): number {",
        "  private addAB(a: number, b: number): number {",
        "  private addText(text: string): number {"
    )]
    public async Task OverloadedMethod_MapsTheDispatcherThroughTheSharedTable(
        string accessibility,
        string signatures,
        string dispatcherHead,
        string firstFastPath,
        string secondFastPath
    )
    {
        var files = TranspileHelper.Transpile(
            $$"""
            namespace App;

            [Transpile]
            public class Engine
            {
                {{accessibility}} int Add(int a, int b) => a + b;
                {{accessibility}} int Add(string text) => text.Length;
            }
            """
        );
        var output = files["app/engine.ts"];

        await Assert.That(output).Contains(signatures + "\n");
        await Assert.That(output).Contains(dispatcherHead + "\n");
        await Assert.That(output).Contains(firstFastPath + "\n");
        await Assert.That(output).Contains(secondFastPath + "\n");
    }

    [Test]
    public async Task OverloadedInternalMethod_CrossClassCall_EmitsAPublicTaggedDispatcher()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Engine
            {
                internal int Add(int a, int b) => a + b;
                internal int Add(string text) => text.Length;
            }

            [Transpile]
            public class Caller
            {
                public int Run() => new Engine().Add(1, 2) + new Engine().Add("x");
            }
            """
        );
        var output = files["app/engine.ts"];

        await Assert.That(output).DoesNotContain("private add(");
        await Assert
            .That(output)
            .Contains("/** @internal */\n  add(a: number, b: number): number;");

        await Assert.That(files["app/caller.ts"]).Contains("new Engine().add(1, 2)");
    }

    [Test]
    public async Task ProtectedInternalMember_CrossClassUse_EmitsPublicTaggedMembers()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Widget
            {
                protected internal int Count { get; set; }

                protected internal void Reset() { }
            }

            [Transpile]
            public class Outsider
            {
                public int Use()
                {
                    var widget = new Widget();

                    widget.Count = 2;
                    widget.Reset();

                    return widget.Count;
                }
            }
            """
        );
        var output = files["app/widget.ts"];

        await Assert.That(output).DoesNotContain("protected count");
        await Assert.That(output).DoesNotContain("protected reset");
        await Assert.That(output).Contains("/** @internal */\n  count: number = 0;");
        await Assert.That(output).Contains("/** @internal */\n  reset(): void");

        await Assert.That(files["app/outsider.ts"]).Contains("widget.count = 2;");
    }

    [Test]
    [Arguments("internal", "  /** @internal */\n  constructor(x: number) { }")]
    [Arguments("protected internal", "  /** @internal */\n  constructor(x: number) { }")]
    [Arguments("protected", "  protected constructor(x: number) { }")]
    [Arguments("public", "  constructor(x: number) { }")]
    [Arguments("private", "  private constructor(x: number) { }")]
    [Arguments("private protected", "  private constructor(x: number) { }")]
    public async Task Constructor_MapsThroughTheSharedTable(
        string accessibility,
        string expectedLine
    )
    {
        var files = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public class Widget
            {
                {{accessibility}} Widget(int x) { }
            }
            """
        );

        await Assert.That(files["widget.ts"]).Contains(expectedLine + "\n");
    }

    [Test]
    public async Task InternalConstructorOverloads_ShareOnePublicTaggedDispatcher()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Gauge
            {
                internal Gauge(int value) { }

                internal Gauge(string text) { }
            }
            """
        );
        var output = files["gauge.ts"];

        await Assert
            .That(output)
            .Contains(
                "  /** @internal */\n  constructor(value: number);\n"
                    + "  /** @internal */\n  constructor(text: string);\n"
                    + "  /** @internal */\n  constructor(...args: unknown[]) {"
            );
        await Assert.That(output).DoesNotContain("private constructor");
    }

    [Test]
    public async Task InternalModuleVariable_ExportsTagged()
    {
        var files = TranspileHelper.TranspileConsoleApp(
            """
            [Transpile, ExportedAsModule]
            public static class Limits
            {
                internal static readonly int Max = 5;

                [ModuleEntryPoint]
                internal static void Main()
                {
                    Console.WriteLine(Limits.Max);
                }
            }
            """
        );

        await Assert
            .That(files["limits.ts"])
            .Contains("/** @internal */\nexport const max: number = 5;");
    }

    [Test]
    public async Task CrossPackageOverrideOfProtectedInternal_EmitsPublicTaggedLikeItsBase()
    {
        var library = """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public class Shape
            {
                protected internal virtual int Sides() => 0;

                protected internal virtual int Size { get; set; }
            }

            public abstract class Figure
            {
                protected internal abstract int Corners();
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Square : Lib.Shape
            {
                protected override int Sides() => 4;

                protected override int Size { get; set; }
            }

            public class Triangle : Lib.Figure
            {
                protected override int Corners() => 3;
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var square = result["app/square.ts"];
        var triangle = result["app/triangle.ts"];

        await Assert.That(square).Contains("  /** @internal */\n  sides(): number {");
        await Assert.That(square).Contains("  /** @internal */\n  get size(): number {");
        await Assert.That(square).Contains("  /** @internal */\n  set size(value: number) {");
        await Assert.That(square).DoesNotContain("protected ");
        await Assert.That(triangle).Contains("  /** @internal */\n  corners(): number {");
        await Assert.That(triangle).DoesNotContain("protected ");
    }
}
