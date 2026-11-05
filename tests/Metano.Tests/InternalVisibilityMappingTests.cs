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
    public async Task FieldPropertyAndMethod_EmitTheirMappedModifierAndInternalTag(
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
    public async Task StaticField_EmitsItsMappedModifierAndInternalTag(
        string accessibility,
        string fieldLine
    )
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
        "  /** @internal */\n  add(a: number, b: number): number;\n  /** @internal */\n  add(text: string): number;\n"
            + "  add(...args: unknown[]): number {",
        "  add(...args: unknown[]): number {",
        "  private addAB(a: number, b: number): number {",
        "  private addText(text: string): number {"
    )]
    [Arguments(
        "protected internal",
        "  /** @internal */\n  add(a: number, b: number): number;\n  /** @internal */\n  add(text: string): number;\n"
            + "  add(...args: unknown[]): number {",
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
    public async Task OverloadedMethod_TagsEachSignatureAndLeavesTheImplementationUntagged(
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
    public async Task Constructor_EmitsItsMappedModifierAndInternalTag(
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
    public async Task InternalConstructorOverloads_TagEachSignatureAndLeaveTheImplementationUntagged()
    {
        var files = TranspileHelper.Transpile(
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
                    + "  constructor(...args: unknown[]) {"
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
    public async Task MixedMethodGroup_TagsOnlyTheInternalSignature()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Engine
            {
                public int Add(int a) => a;

                internal int Add(int a, int b) => a + b;
            }
            """
        );

        await Assert
            .That(files["app/engine.ts"])
            .Contains(
                "  /** @internal */\n  add(a: number, b: number): number;\n"
                    + "  add(a: number): number;\n"
                    + "  add(...args: unknown[]): number {"
            );
    }

    [Test]
    [Arguments("private")]
    [Arguments("protected")]
    [Arguments("internal")]
    public async Task MixedMethodGroup_EmitsTheMostPermissiveDispatcher(string widerOverload)
    {
        var files = TranspileHelper.Transpile(
            $$"""
            namespace App;

            [Transpile]
            public class Engine
            {
                public int Add(int a) => a;

                {{widerOverload}} int Add(int a, int b) => a + b;
            }
            """
        );
        var output = files["app/engine.ts"];

        await Assert.That(output).Contains("\n  add(...args: unknown[]): number {");
        await Assert.That(output).DoesNotContain($"{widerOverload} add(");
    }

    [Test]
    public async Task ProtectedAndInternalMethodGroup_EmitsAPublicDispatcher()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Engine
            {
                protected int Add(int a) => a;

                internal int Add(int a, int b) => a + b;
            }
            """
        );
        var output = files["app/engine.ts"];

        await Assert
            .That(output)
            .Contains(
                "  /** @internal */\n  add(a: number, b: number): number;\n"
                    + "  add(a: number): number;\n"
                    + "  add(...args: unknown[]): number {"
            );
        await Assert.That(output).DoesNotContain("protected add(");
    }

    [Test]
    [Arguments("public Gauge(int value) { }", "internal Gauge(string text) { }")]
    [Arguments("internal Gauge(string text) { }", "public Gauge(int value) { }")]
    public async Task MixedConstructorGroup_TagsOnlyTheInternalSignatureInEitherOrder(
        string first,
        string second
    )
    {
        var files = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public class Gauge
            {
                {{first}}

                {{second}}
            }
            """
        );

        var output = files["gauge.ts"];

        await Assert.That(output).Contains("  /** @internal */\n  constructor(text: string);\n");
        await Assert.That(output).Contains("  constructor(value: number);\n");
        await Assert.That(output).DoesNotContain("/** @internal */\n  constructor(value");
        await Assert.That(output).DoesNotContain("/** @internal */\n  constructor(...args");
    }

    [Test]
    public async Task ProtectedAndInternalConstructorGroup_EmitsAPublicDispatcher()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Gauge
            {
                protected Gauge(int value) { }

                internal Gauge(string text) { }
            }
            """
        );
        var output = files["gauge.ts"];

        await Assert
            .That(output)
            .Contains(
                "  constructor(value: number);\n"
                    + "  /** @internal */\n  constructor(text: string);\n"
                    + "  constructor(...args: unknown[]) {"
            );
        await Assert.That(output).DoesNotContain("protected constructor");
    }

    [Test]
    public async Task InternalValueTypeConstructor_StaysPublicAndUntagged()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Meter
            {
                public int Value;

                internal Meter(int value) { Value = value; }
            }
            """
        );
        var output = files["meter.ts"];

        await Assert.That(output).Contains("  constructor(value: number)");
        await Assert.That(output).DoesNotContain("/** @internal */\n  constructor");
    }

    [Test]
    public async Task InternalEvent_TagsItsAddAndRemoveAccessors()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public class Clock
            {
                internal event Action? Ticked;

                public void Tick() => Ticked?.Invoke();
            }
            """
        );
        var output = files["app/clock.ts"];

        await Assert.That(output).Contains("  /** @internal */\n  ticked$add(handler:");
        await Assert.That(output).Contains("  /** @internal */\n  ticked$remove(handler:");
        await Assert.That(output).Contains("  private ticked:");
    }

    [Test]
    public async Task ProtectedInternalPromotedParameter_TagsTheParameterProperty()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Probe(int x)
            {
                protected internal int X { get; } = x;
            }
            """
        );

        await Assert
            .That(files["app/probe.ts"])
            .Contains("  constructor(/** @internal */ readonly x: number) { }");
    }

    [Test]
    public async Task PrivateProtectedPromotedParameter_EmitsAProtectedParameterProperty()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Probe(int x)
            {
                private protected int X { get; } = x;
            }

            [Transpile]
            public class DerivedProbe(int x) : Probe(x)
            {
                public int Twice() => X * 2;
            }
            """
        );

        await Assert
            .That(files["app/probe.ts"])
            .Contains("  constructor(protected readonly x: number) { }");
        await Assert.That(files["app/derived-probe.ts"]).Contains("return this.x * 2;");
    }

    [Test]
    public async Task PrivateProtectedMembers_AreStillSkipped()
    {
        var files = TranspileHelper.Transpile(
            """
            using System;

            namespace App;

            [Transpile]
            public class Widget
            {
                private protected int Count;

                private protected int Steps { get; set; }

                private protected event Action? Changed;

                private protected void Reset() { }

                public int Read() => 1;
            }
            """
        );
        var output = files["app/widget.ts"];

        await Assert.That(output).DoesNotContain("count");
        await Assert.That(output).DoesNotContain("steps");
        await Assert.That(output).DoesNotContain("changed");
        await Assert.That(output).DoesNotContain("reset");
        await Assert.That(output).Contains("  read(): number {");
    }

    [Test]
    public async Task InternalModuleFunction_IsNotEmitted()
    {
        // Known limitation (FR-049 note / matrix): module-function extraction
        // admits public members only, so an internal function of an
        // [ExportedAsModule] class is dropped rather than tagged.
        var files = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Counters
            {
                public static int Twice(int value) => value * 2;

                internal static int Inc(int value) => value + 1;
            }
            """
        );
        var output = files["counters.ts"];

        await Assert.That(output).Contains("export function twice(value: number): number {");
        await Assert.That(output).DoesNotContain("inc(");
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

    [Test]
    public async Task MixedVisibilityOverloadsAndConstructors_MatchTheGolden()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Ledger
            {
                public Ledger(int seed) { }

                internal Ledger(string name) { }

                public int Post(int amount) => amount;

                internal int Post(int amount, string memo) => amount + memo.Length;

                protected internal int Audit(int amount) => amount;

                protected internal int Audit(string memo) => memo.Length;
            }
            """
        );

        await Assert
            .That(files["app/ledger.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("mixed-visibility-overloads.ts"));
    }
}
