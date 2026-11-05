namespace Metano.Tests;

/// <summary>
/// Issue #445: one shared visibility table for every TypeScript emission site.
/// C# <c>internal</c> widens to a public TypeScript member tagged
/// <c>/** @internal *&#47;</c> — same-assembly callers reach it from any
/// class, so the emitted surface must not restrict it; <c>protected
/// internal</c> widens to an untagged public member, since a derived class in
/// another package may use it; <c>private protected</c> becomes a
/// <c>protected</c> member tagged <c>/** @internal *&#47;</c>; <c>private</c>
/// stays <c>private</c> and <c>protected</c> stays <c>protected</c>.
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
        "{\n  count: number = 0;",
        "\n\n  steps: number = 0;",
        "\n\n  reset(): void"
    )]
    [Arguments(
        "private protected",
        "  /** @internal */\n  protected count: number = 0;",
        "  /** @internal */\n  protected steps: number = 0;",
        "  /** @internal */\n  protected reset(): void"
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
    [Arguments("protected internal", "{\n  static value: number = 0;")]
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
        "}\n\n  add(a: number, b: number): number;\n  add(text: string): number;\n"
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
    public async Task ProtectedInternalMember_CrossClassUse_EmitsPublicUntaggedMembers()
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

        await Assert.That(output).Contains("  count: number = 0;");
        await Assert.That(output).Contains("  reset(): void");
        await Assert.That(output).DoesNotContain("protected count:");
        await Assert.That(output).DoesNotContain("protected reset(");
        await Assert.That(output).DoesNotContain("@internal");

        await Assert.That(files["app/outsider.ts"]).Contains("widget.count = 2;");
    }

    [Test]
    [Arguments("internal", "  constructor(x: number) { }")]
    [Arguments("protected internal", "  constructor(x: number) { }")]
    [Arguments("protected", "  protected constructor(x: number) { }")]
    [Arguments("public", "  constructor(x: number) { }")]
    [Arguments("private", "  private constructor(x: number) { }")]
    [Arguments("private protected", "  private constructor(x: number) { }")]
    public async Task SingleConstructor_EmitsItsMappedModifierUntagged(
        string accessibility,
        string expectedLine
    )
    {
        // A single constructor is the only signature the .d.ts can keep:
        // tagging an internal one would let stripInternal leave an implicit
        // public `new Widget()` that C# never declared, so it is widened.
        var files = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public class Widget
            {
                {{accessibility}} Widget(int x) { }
            }
            """
        );
        var output = files["widget.ts"];

        await Assert.That(output).Contains(expectedLine + "\n");
        await Assert.That(output).DoesNotContain("@internal");
    }

    [Test]
    public async Task AllInternalConstructorGroup_KeepsEverySignatureUntagged()
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
                "  constructor(value: number);\n"
                    + "  constructor(text: string);\n"
                    + "  constructor(...args: unknown[]) {"
            );
        await Assert.That(output).DoesNotContain("@internal */\n  constructor(");
        await Assert.That(output).DoesNotContain("private constructor(");
        // Each factory keeps its own overload's visibility (spec 013 DECIDED-17).
        await Assert
            .That(output)
            .Contains("  /** @internal */\n  static fromValue(value: number): Gauge {");
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
    [Arguments("private", "  /** @internal */\n  add(a: number, b: number): number;\n")]
    [Arguments("internal", "  /** @internal */\n  add(a: number, b: number): number;\n")]
    [Arguments("private protected", "  /** @internal */\n  add(a: number, b: number): number;\n")]
    [Arguments("protected", "}\n\n  add(a: number, b: number): number;\n")]
    public async Task MixedMethodGroup_TagsOnlyTheSignaturesNoOtherPackageReaches(
        string narrowerOverload,
        string expectedSignatures
    )
    {
        // A protected overload of a public group is widened and left untagged:
        // a derived class in another package may call it.
        var files = TranspileHelper.Transpile(
            $$"""
            namespace App;

            [Transpile]
            public class Engine
            {
                public int Add(int a) => a;

                {{narrowerOverload}} int Add(int a, int b) => a + b;
            }
            """
        );

        await Assert
            .That(files["app/engine.ts"])
            .Contains(
                expectedSignatures
                    + "  add(a: number): number;\n"
                    + "  add(...args: unknown[]): number {"
            );
    }

    [Test]
    public async Task ProtectedMethodGroup_TagsItsPrivateSignature()
    {
        var files = TranspileHelper.Transpile(
            """
            namespace App;

            [Transpile]
            public class Engine
            {
                protected int Add(int a) => a;

                private int Add(int a, int b) => a + b;
            }
            """
        );

        await Assert
            .That(files["app/engine.ts"])
            .Contains(
                "  /** @internal */\n  protected add(a: number, b: number): number;\n"
                    + "  protected add(a: number): number;\n"
                    + "  protected add(...args: unknown[]): number {"
            );
    }

    [Test]
    public async Task ProtectedAndInternalMethodGroup_TagsOnlyTheInternalSignature()
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
    [Arguments("private", "  /** @internal */\n  constructor(text: string);\n")]
    [Arguments("protected", "  constructor(value: number);\n  constructor(text: string);\n")]
    public async Task MixedConstructorGroup_TagsOnlyAPrivateNarrowerSignature(
        string narrower,
        string expectedSignatures
    )
    {
        var files = TranspileHelper.Transpile(
            $$"""
            [Transpile]
            public class Gauge
            {
                public Gauge(int value) { }

                {{narrower}} Gauge(string text) { }
            }
            """
        );

        await Assert
            .That(files["gauge.ts"])
            .Contains(expectedSignatures + "  constructor(...args: unknown[]) {");
    }

    [Test]
    public async Task ProtectedAndInternalConstructorGroup_TagsOnlyTheInternalSignature()
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

        // The protected constructor is widened and stays in the .d.ts, so a
        // derived class in another package can still call `super(1)`.
        await Assert
            .That(output)
            .Contains(
                "  constructor(value: number);\n"
                    + "  /** @internal */\n  constructor(text: string);\n"
                    + "  constructor(...args: unknown[]) {"
            );
        await Assert.That(output).DoesNotContain("protected constructor(");
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
    public async Task ProtectedInternalPromotedParameter_EmitsAnUntaggedParameterProperty()
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
        var output = files["app/probe.ts"];

        await Assert.That(output).Contains("  constructor(readonly x: number) { }");
        await Assert.That(output).DoesNotContain("@internal");
    }

    [Test]
    public async Task PrivateProtectedPromotedParameter_EmitsAProtectedTaggedParameterProperty()
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
            .Contains("  constructor(/** @internal */ protected readonly x: number) { }");
        await Assert.That(files["app/derived-probe.ts"]).Contains("return this.x * 2;");
    }

    [Test]
    public async Task PrivateProtectedMembers_EmitProtectedTaggedForTheirOwnAndDerivedBodies()
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

                public int Read()
                {
                    Changed?.Invoke();
                    Reset();

                    return Count + Steps;
                }
            }

            [Transpile]
            public class Gadget : Widget
            {
                public int Bump()
                {
                    Reset();

                    return Count + Steps;
                }
            }
            """
        );
        var widget = files["app/widget.ts"];
        var gadget = files["app/gadget.ts"];

        await Assert.That(widget).Contains("  /** @internal */\n  protected count: number = 0;");
        await Assert.That(widget).Contains("  /** @internal */\n  protected steps: number = 0;");
        await Assert.That(widget).Contains("  private changed: ");
        await Assert.That(widget).Contains("  /** @internal */\n  protected changed$add(handler:");
        await Assert
            .That(widget)
            .Contains("  /** @internal */\n  protected changed$remove(handler:");
        await Assert.That(widget).Contains("  /** @internal */\n  protected reset(): void {");
        await Assert.That(widget).Contains("this.changed?.();");
        await Assert.That(widget).Contains("this.reset();");
        await Assert.That(widget).Contains("return this.count + this.steps;");
        await Assert.That(gadget).Contains("this.reset();");
        await Assert.That(gadget).Contains("return this.count + this.steps;");
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
        await Assert.That(output).DoesNotContain("function inc(");
    }

    [Test]
    public async Task CrossPackageOverrideOfProtectedInternal_EmitsPublicLikeItsBase()
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

        await Assert.That(square).Contains("  sides(): number {");
        await Assert.That(square).Contains("  get size(): number {");
        await Assert.That(square).Contains("  set size(value: number) {");
        await Assert.That(square).DoesNotContain("protected ");
        await Assert.That(square).DoesNotContain("@internal");
        await Assert.That(triangle).Contains("  corners(): number {");
        await Assert.That(triangle).DoesNotContain("protected ");
        await Assert.That(triangle).DoesNotContain("@internal");
    }

    [Test]
    public async Task CrossPackageSubclass_UsesProtectedInternalMembersAndConstructor()
    {
        // Pins the emitted shape a tsgo probe validated: the library's
        // stripInternal .d.ts keeps `v()`, `p` and the `protected internal`
        // constructor signature, so the cross-package subclass type-checks.
        // The consumer assertions below are a smoke check only.
        var library = """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public class Base
            {
                protected internal Base(string name) { }

                internal Base(int id) { }

                protected internal virtual void V() { }

                protected internal int P { get; set; }
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Derived : Lib.Base
            {
                public Derived() : base("x") { }

                public int Read()
                {
                    V();

                    return P;
                }
            }
            """;

        var result = TranspileHelper.TranspileWithLibrary(library, consumer);
        var derived = result["app/derived.ts"];

        await Assert.That(derived).Contains("super(\"x\");");
        await Assert.That(derived).Contains("this.v();");
        await Assert.That(derived).Contains("return this.p;");

        var baseOutput = TranspileHelper.Transpile(library)["lib/base.ts"];

        await Assert
            .That(baseOutput)
            .Contains(
                "  constructor(name: string);\n"
                    + "  /** @internal */\n  constructor(id: number);\n"
                    + "  constructor(...args: unknown[]) {"
            );
        await Assert.That(baseOutput).Contains("\n  v(): void {");
        await Assert.That(baseOutput).DoesNotContain("protected v(");
        await Assert.That(baseOutput).DoesNotContain("/** @internal */\n  v(");
        await Assert.That(baseOutput).Contains("\n  p: number = 0;");
        await Assert.That(baseOutput).DoesNotContain("/** @internal */\n  p:");
    }

    [Test]
    public async Task CrossPackageSubclass_CallsProtectedOverloadAndConstructorOfMixedGroups()
    {
        // Pins the emitted shape a tsgo probe validated: the library's
        // stripInternal .d.ts keeps the protected `q(a: string)` signature and
        // `constructor()`, so the cross-package subclass type-checks. The
        // library assertions catch the regression; the consumer ones are a
        // smoke check only.
        var library = """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            namespace Lib;

            public class Base
            {
                public Base(int seed) { }

                protected Base() { }

                public int Q(int a) => a;

                protected int Q(string a) => a.Length;
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Derived : Lib.Base
            {
                public Derived() : base() { }

                public int Go() => Q("x") + Q(1);
            }
            """;

        var derived = TranspileHelper.TranspileWithLibrary(library, consumer)["app/derived.ts"];

        await Assert.That(derived).Contains("super();");
        await Assert.That(derived).Contains("this.q(\"x\") + this.q(1)");

        var baseOutput = TranspileHelper.Transpile(library)["lib/base.ts"];

        await Assert
            .That(baseOutput)
            .Contains(
                "  constructor(seed: number);\n"
                    + "  constructor();\n"
                    + "  constructor(...args: unknown[]) {"
            );
        await Assert
            .That(baseOutput)
            .Contains(
                "}\n\n  q(a: number): number;\n"
                    + "  q(a: string): number;\n"
                    + "  q(...args: unknown[]): number {"
            );
        await Assert.That(baseOutput).DoesNotContain("@internal");
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

                protected int Audit(string memo) => memo.Length;
            }
            """
        );

        await Assert
            .That(files["app/ledger.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("mixed-visibility-overloads.ts"));
    }
}
