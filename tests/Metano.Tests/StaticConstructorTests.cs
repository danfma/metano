using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A static constructor lowers to a class <c>static { }</c> block that runs
/// after the static fields, the order C# runs it in; before, its body was
/// dropped with no diagnostic (#426).
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

            private static int Init(string text)
            {
                Log.Add(text);

                return 1;
            }
        }
        """;

    [Test]
    public async Task StaticConstructor_BecomesAStaticBlockAfterTheStaticFields()
    {
        var output = TranspileHelper.Transpile(Registry)["registry.ts"];

        await Assert.That(output).Contains("static {");
        await Assert.That(output).Contains("Registry.instance = new Registry();");
        await Assert.That(output).Contains("const n = 1;");

        var block = output.IndexOf("static {", StringComparison.Ordinal);

        await Assert
            .That(block)
            .IsGreaterThan(output.IndexOf("static b:", StringComparison.Ordinal));

        await Assert
            .That(block)
            .IsGreaterThan(output.IndexOf("static a:", StringComparison.Ordinal));
    }

    [Test]
    public async Task StaticReadonlyField_TheStaticConstructorWrites_IsMutable()
    {
        var output = TranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public static class Table
            {
                public static readonly Dictionary<string, int> Map;
                public static readonly int Count;
                public static readonly int Seed;
                public static readonly int Untouched = 3;

                static Table()
                {
                    Map = new Dictionary<string, int>();
                    Map["a"] = 1;
                    Count += 2;
                    int.TryParse("4", out Seed);
                }
            }
            """
        )["table.ts"];

        await Assert.That(output).Contains("static map: Map<string, number>;");
        await Assert.That(output).Contains("static count: number = 0;");
        await Assert.That(output).Contains("static seed: number = 0;");
        await Assert.That(output).Contains("static readonly untouched: number = 3;");
    }

    [Test]
    public async Task GetOnlyStaticProperty_TheStaticConstructorWrites_IsMutable()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public class RoSingle
            {
                public static RoSingle Instance { get; }
                public static int P { get; }
                public static int Untouched { get; } = 3;
                public int V = 4;

                static RoSingle()
                {
                    Instance = new RoSingle();
                    P = 9;
                }
            }
            """
        )["ro-single.ts"];

        await Assert.That(output).Contains("static instance: RoSingle;");
        await Assert.That(output).Contains("static p: number = 0;");
        await Assert.That(output).Contains("static readonly untouched: number = 3;");
    }

    [Test]
    public async Task TypeWithoutAStaticConstructor_EmitsNoStaticBlock()
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

        await Assert.That(output).DoesNotContain("static {");
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
}
