using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

public class StaticConstructorCycleDiagnosticTests
{
    [Test]
    public async Task StaticConstructorOnlyMethodCalls_ReportMs0005()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo;

            [Transpile]
            public class First
            {
                public static int Value;

                static First() { Value = Second.Read() + 1; }

                public static int Read() => Value;
            }

            [Transpile]
            public class Second
            {
                public static int Value;

                static Second() { Value = First.Read() + 5; }

                public static int Read() => Value;
            }
            """
        );

        var first = files.Single(file => file.Key.EndsWith("/first.ts")).Value;
        var second = files.Single(file => file.Key.EndsWith("/second.ts")).Value;
        await Assert.That(first).Contains("import { Second }");
        await Assert.That(second).Contains("import { First }");

        var cycles = diagnostics.Where(diagnostic => diagnostic.Code == "MS0005").ToList();
        await Assert.That(cycles.Count).IsEqualTo(1);
        await Assert.That(cycles[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(cycles[0].Message).Contains("first");
        await Assert.That(cycles[0].Message).Contains("second");
    }

    [Test]
    public async Task StaticConstructorOnlyObjectCreation_ReportMs0005UnderCustomAlias()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo;

            [Transpile]
            public class First
            {
                static First() { _ = new Second(); }
            }

            [Transpile]
            public class Second
            {
                static Second() { _ = new First(); }
            }
            """,
            importAlias: "contracts"
        );

        var cycles = diagnostics.Where(diagnostic => diagnostic.Code == "MS0005").ToList();
        await Assert.That(cycles.Count).IsEqualTo(1);
        await Assert.That(cycles[0].Message).Contains("#contracts/");
        await Assert.That(cycles[0].Message).Contains("first");
        await Assert.That(cycles[0].Message).Contains("second");
    }

    [Test]
    public async Task StaticConstructorOnlyFieldReadsAcrossNamespaces_ReportMs0005()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo.Left
            {
                [Transpile]
                public class First
                {
                    public static int Value;

                    static First() { Value = Demo.Right.Second.Value + 1; }
                }
            }

            namespace Demo.Right
            {
                [Transpile]
                public class Second
                {
                    public static int Value;

                    static Second() { Value = Demo.Left.First.Value + 5; }
                }
            }
            """
        );

        var cycles = diagnostics.Where(diagnostic => diagnostic.Code == "MS0005").ToList();
        await Assert.That(cycles.Count).IsEqualTo(1);
        await Assert.That(cycles[0].Message).Contains("first");
        await Assert.That(cycles[0].Message).Contains("second");
    }

    [Test]
    public async Task StaticConstructorWithoutCycle_DoesNotReportMs0005()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            namespace Demo;

            [Transpile]
            public class First
            {
                public static int Value;

                static First() { Value = Second.Read(); }
            }

            [Transpile]
            public class Second
            {
                public static int Read() => 7;
            }
            """
        );

        await Assert.That(diagnostics.Any(diagnostic => diagnostic.Code == "MS0005")).IsFalse();
    }
}
