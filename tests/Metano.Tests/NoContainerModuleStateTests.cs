using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// Static fields and auto-properties of a containerless static class
/// (<c>[NoContainer]</c> / <c>[ExportedAsModule]</c>) become module variables:
/// call sites already read them as bare identifiers, so the module declares and
/// exports them, and other files import them (#314).
/// </summary>
public class NoContainerModuleStateTests
{
    [Test]
    public async Task StaticFieldsAndAutoProperties_EmitModuleVariables()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Limits
            {
                public static readonly int Max = 10;
                public const int Min = 1;
                public static int Hits = 5;
                public static decimal Total;
                public static string? Label;
                public static int Step { get; } = 2;
                public static bool Enabled { get; set; }
                private static readonly int Secret = 7;

                [Name("ceiling")]
                public static readonly int Upper = 100;

                [Ignore]
                public static readonly int Hidden = 0;

                public static void Hit()
                {
                    Hits++;
                    Hits += Step;
                    Total = Total + 1m;
                }

                public static int Clamp(int v) => v > Max ? Max : v < Min ? Min : v + Secret + Upper;
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("no-container-module-state.ts");
        await Assert.That(result["limits.ts"]).IsEqualTo(expected);
    }

    [Test]
    public async Task ExportedAsModule_EmitsModuleVariables()
    {
        var result = TranspileHelper.Transpile(
            """
            #pragma warning disable CS0618
            [Transpile, ExportedAsModule]
            public static class Greetings
            {
                public static readonly string Hello = "hi";

                public static string Say() => Hello;
            }
            """
        );

        var output = result["greetings.ts"];
        await Assert.That(output).Contains("export const hello: string = \"hi\";");
        await Assert.That(output).Contains("return hello;");
    }

    [Test]
    public async Task ReadFromAnotherFile_ImportsModuleVariable()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Limits
            {
                public static readonly int Max = 10;
                public static int Step { get; } = 2;
            }

            [Transpile]
            public class Meter
            {
                public int Read() => Limits.Max + Limits.Step;
            }
            """
        );

        var output = result["meter.ts"];
        await Assert.That(output).Contains("import { max, step } from \"./limits\";");
        await Assert.That(output).Contains("return max + step;");
    }

    [Test]
    public async Task LocalWithSameName_IsNotImported()
    {
        // The import comes from the flattened member access, never from a bare
        // identifier, so a local that shares the module variable's name stays local.
        var result = TranspileHelper.Transpile(
            """
            [Transpile, NoContainer]
            public static class Limits
            {
                public static readonly int Max = 10;
            }

            [Transpile]
            public class Meter
            {
                public int Read()
                {
                    var max = 3;
                    return max;
                }
            }
            """
        );

        await Assert.That(result["meter.ts"]).DoesNotContain("import");
    }

    [Test]
    public async Task WriteFromAnotherFile_ReportsUnsupported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Counters
            {
                public static int Hits;
            }

            [Transpile]
            public class Clicker
            {
                public void Click() => Counters.Hits++;

                public void Reset() => Counters.Hits = 0;
            }
            """
        );

        var writes = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature && d.Message.Contains("'hits'")
            )
            .ToList();
        await Assert.That(writes.Count).IsEqualTo(1);
        await Assert.That(files["counters.ts"]).Contains("export let hits: number = 0;");
    }

    [Test]
    public async Task WriteInsideDeclaringFile_IsAllowed()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Counters
            {
                public static int Hits;

                public static void Hit() => Hits++;
            }
            """
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsFalse();
    }

    [Test]
    public async Task ComputedStaticProperty_ReportsUnsupported()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
            public static class Limits
            {
                public static readonly int Max = 10;

                public static int Twice => Max * 2;
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("'Limits.Twice'")
                )
            )
            .IsTrue();
        await Assert.That(files["limits.ts"]).DoesNotContain("twice");
    }

    [Test]
    public async Task CrossPackageRead_ImportsFromPackage()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib.Config;

            [NoContainer]
            public static class Limits
            {
                public static readonly int Max = 10;
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Meter
            {
                public int Read() => MyLib.Config.Limits.Max;
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["app/meter.ts"];

        await Assert.That(output).Contains("import { max } from \"@scope/lib/my-lib/config\";");
        await Assert.That(output).Contains("return max;");
    }

    [Test]
    public async Task CrossPackageWrite_ReportsUnsupported()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@scope/lib")]

            namespace MyLib.Config;

            [NoContainer]
            public static class Counters
            {
                public static int Hits;
            }
            """;

        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Clicker
            {
                public void Click() => MyLib.Config.Counters.Hits = 1;
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            library,
            consumer
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.UnsupportedFeature && d.Message.Contains("'hits'")
                )
            )
            .IsTrue();
    }
}
