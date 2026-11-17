using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A type that declares a static constructor runs it lazily, as .NET does
/// (spec 001 FR-066, #681): a <c>typeInitializer</c> guard holds the static
/// initializers and the body, static fields and auto-properties become
/// <c>#name$</c> slots behind guarded accessors, and static members and
/// constructors call the guard first. Before #681 the body ran in a class
/// <c>static { }</c> block at load (#426).
/// </summary>
public class StaticConstructorTests
{
    private const string Registry = """
        using System.Collections.Generic;

        [Transpile]
        public sealed class Registry
        {
            public static readonly List<string> Log = new();

            public static int A = Init("A");

            public static readonly Registry Instance;

            static Registry()
            {
                Log.Add("cctor");
                Instance = new Registry();
                var n = 1;
                Log.Add("n" + n);
            }

            public static int B = Init("B");

            public static int Untouched;

            private static int Init(string text)
            {
                Log.Add(text);

                return 1;
            }
        }
        """;

    [Test]
    public async Task StaticConstructor_LowersToALazyTypeInitializer()
    {
        var output = TranspileHelper.Transpile(Registry)["registry.ts"];

        await TranspileHelper.AssertGolden("lazy-static-constructor.ts", output);
    }

    [Test]
    public async Task StaticConstructor_RunsTheInitializersInTextualOrderThenTheBody()
    {
        var output = TranspileHelper.Transpile(Registry)["registry.ts"];

        var log = output.IndexOf("Registry.#log$ = ", StringComparison.Ordinal);
        var a = output.IndexOf("Registry.#a$ = Registry.init(\"A\");", StringComparison.Ordinal);
        var b = output.IndexOf("Registry.#b$ = Registry.init(\"B\");", StringComparison.Ordinal);
        var body = output.IndexOf("Registry.log.push(\"cctor\");", StringComparison.Ordinal);

        await Assert.That(log).IsGreaterThan(0);
        await Assert.That(a).IsGreaterThan(log);
        await Assert.That(b).IsGreaterThan(a);
        await Assert.That(body).IsGreaterThan(b);
        await Assert.That(output).DoesNotContain("static {");
    }

    [Test]
    public async Task ReadOnlyStatic_TheStaticConstructorWrites_GetsAPrivateSetter()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class RoSingle
            {
                public static RoSingle Instance { get; }
                public static int Untouched { get; } = 3;
                public static readonly int Seed;

                static RoSingle()
                {
                    Instance = new RoSingle();
                    Seed = 9;
                }
            }
            """
        )["ro-single.ts"];

        await Assert.That(output).Contains("static #instance$: RoSingle | null = null;");
        await Assert.That(output).Contains("return RoSingle.#instance$ as RoSingle;");
        await Assert.That(output).Contains("private static set instance(value: RoSingle) {");
        await Assert.That(output).Contains("private static set seed(value: number) {");
        await Assert.That(output).Contains("static get untouched(): number {");
        await Assert.That(output).DoesNotContain("set untouched(");
    }

    [Test]
    public async Task NonLiteralSlotDefault_IsAssignedFirstInTheInitializer()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Mode { Off, On }

            [Transpile]
            public static class Settings
            {
                public static Mode Current;
                public static string? Label;

                static Settings() { Label = "x"; }
            }
            """
        )["settings.ts"];

        await Assert.That(output).Contains("static #current$: Mode;");
        await Assert.That(output).Contains("static #label$: string | null = null;");

        var assignment = output.IndexOf("Settings.#current$ = Mode.Off;", StringComparison.Ordinal);
        var body = output.IndexOf("Settings.label = \"x\";", StringComparison.Ordinal);

        await Assert.That(assignment).IsGreaterThan(0);
        await Assert.That(body).IsGreaterThan(assignment);
    }

    [Test]
    public async Task BaseClassInstanceFieldInitializer_RunsAfterTheGuard()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Counter
            {
                public static int Next = 1;
                public static int Take() => Next++;
                public static void Reset() => Next = 100;
            }

            [Transpile]
            public class FieldOrder
            {
                public string Name;
                public int A = Counter.Take();
                public int B = Counter.Take();

                static FieldOrder() { Counter.Reset(); }
            }
            """
        )["field-order.ts"];

        await TranspileHelper.AssertGolden("lazy-static-constructor-field-order.ts", output);
    }

    [Test]
    public async Task DerivedClass_RunsTheGuardBeforeSuper()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Base { }

            [Transpile]
            public class Derived : Base
            {
                public int A = 1;

                static Derived() { }
            }
            """
        )["derived.ts"];

        await Assert.That(output).Contains("Derived.#$init();\n    super();");
        await Assert.That(output).Contains("a: number = 1;");
    }

    [Test]
    public async Task Struct_GuardsDeclaredConstructionAndInstanceMembersOnly()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Seed
            {
                public static int Next() => 4;
            }

            [Transpile]
            public struct Cell
            {
                public int V;
                public int W = Seed.Next();

                static Cell() { }

                public Cell(int v) { V = v; }

                public int Get() => V;

                public int Twice => V * 2;
            }
            """
        )["cell.ts"];

        await TranspileHelper.AssertGolden("lazy-static-constructor-struct.ts", output);
    }

    [Test]
    public async Task StaticMembers_StartWithTheGuard_ConstsDoNot()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Ops
            {
                public const int Zero = 0;
                public int V;

                static Ops() { }

                public static int Twice(int x) => x * 2;

                public static int Computed => 3;

                public static Ops operator +(Ops a, Ops b) => new Ops { V = a.V + b.V };
            }
            """
        )["ops.ts"];

        await Assert.That(output).Contains("static readonly zero: number = 0;");
        await Assert.That(output).Contains("static twice(x: number): number {\n    Ops.#$init();");
        await Assert.That(output).Contains("static get computed(): number {\n    Ops.#$init();");

        await Assert
            .That(output)
            .Contains("static __add(a: Ops, b: Ops): Ops {\n    Ops.#$init();");
    }

    [Test]
    public async Task StaticFieldSharingAPropertyName_KeepsOneAccessorPair()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Tally
            {
                static int count;

                static Tally() { }

                public Tally() { count++; }

                public static int Count => count;
            }
            """
        )["tally.ts"];

        await Assert.That(CountOf(output, "get count()")).IsEqualTo(1);
        await Assert.That(output).Contains("return Tally.#count$;");
    }

    [Test]
    public async Task TypeWithoutAStaticConstructor_KeepsEagerStaticFields()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Plain
            {
                public static int A = 1;
            }
            """
        )["plain.ts"];

        await Assert.That(output).Contains("static a: number = 1;");
        await Assert.That(output).DoesNotContain("#$init");
        await Assert.That(output).DoesNotContain("static {");
    }

    [Test]
    public async Task NestedType_ReportsItsDottedName()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Outer
            {
                [Transpile]
                public class Inner
                {
                    static Inner() { }
                }
            }
            """
        )["outer.ts"];

        await Assert.That(output).Contains("typeInitializer(\"Outer.Inner\", () => {");
    }

    [Test]
    public async Task Barrel_ListsTheDerivedModuleOfAnInitializerCycleFirst()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public static class Atlas
            {
                public static string Name() => Shape.Default.Name;
            }

            public class Shape
            {
                public static Shape Default { get; }

                static Shape() { Default = Square.Make(); }

                public virtual string Name => "shape";
            }

            public class Square : Shape
            {
                public static Square Make() => new Square();

                public override string Name => "square";
            }
            """
        );

        var barrel = result["index.ts"];
        var square = barrel.IndexOf("./square", StringComparison.Ordinal);

        await Assert.That(square).IsGreaterThanOrEqualTo(0);
        await Assert.That(square).IsLessThan(barrel.IndexOf("./atlas", StringComparison.Ordinal));
        await Assert.That(square).IsLessThan(barrel.IndexOf("./shape", StringComparison.Ordinal));
    }

    [Test]
    public async Task Barrel_WithoutAnInitializerCycle_KeepsItsOrder()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Shape
            {
                public static int Count;

                static Shape() { Count = 1; }
            }

            public class Square : Shape { }

            public class Atlas { }
            """
        );

        var barrel = result["index.ts"];

        await Assert
            .That(barrel.IndexOf("./atlas", StringComparison.Ordinal))
            .IsLessThan(barrel.IndexOf("./shape", StringComparison.Ordinal));

        await Assert
            .That(barrel.IndexOf("./shape", StringComparison.Ordinal))
            .IsLessThan(barrel.IndexOf("./square", StringComparison.Ordinal));
    }

    [Test]
    public async Task EmitInFile_PrintsTheBaseBeforeADerivedClassDeclaredFirst()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile, EmitInFile("pair")]
            public class PairDerived : PairBase { }

            [Transpile, EmitInFile("pair")]
            public class Other { }

            [Transpile, EmitInFile("pair")]
            public class PairBase { }
            """
        )["pair.ts"];

        var other = output.IndexOf("export class Other", StringComparison.Ordinal);
        var baseClass = output.IndexOf("export class PairBase", StringComparison.Ordinal);
        var derived = output.IndexOf("export class PairDerived", StringComparison.Ordinal);

        await Assert.That(other).IsLessThan(baseClass);
        await Assert.That(baseClass).IsLessThan(derived);
    }

    [Test]
    public async Task TypeInitializationException_IsTheRuntimeClass()
    {
        var output = TranspileHelper.Transpile(
            """
            using System;

            [Transpile]
            public static class Probe
            {
                public static bool Failed(Action action)
                {
                    try
                    {
                        action();
                        return false;
                    }
                    catch (TypeInitializationException e)
                    {
                        return e.TypeName.Length > 0;
                    }
                }
            }
            """
        )["probe.ts"];

        await Assert.That(output).Contains("instanceof TypeInitializationException");
        await Assert.That(output).Contains("e.typeName.length");

        await Assert
            .That(output)
            .Contains("from \"metano-runtime/system/exceptions/type-initialization-exception\"");
    }

    [Test]
    public async Task ContainerlessClass_KeepsItsStaticConstructorReport()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Module
            {
                public static int X = 5;

                static Module() { X = 7; }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("static constructor")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Dart_ReportsAStaticConstructor()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public sealed class Counter
            {
                public static int Total;

                static Counter() { Total = 1; }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("static constructor of 'Counter'")
                )
            )
            .IsTrue();
    }

    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
}
