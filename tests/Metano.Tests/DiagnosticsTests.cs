using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

public class DiagnosticsTests
{
    [Test]
    public async Task UnsupportedStatement_GeneratesWarning()
    {
        // `lock` statement is not supported by the transpiler
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Counter
            {
                private readonly object _gate = new();
                private int _value;

                public void Increment()
                {
                    lock (_gate)
                    {
                        _value++;
                    }
                }
            }
            """
        );

        await Assert.That(diagnostics.Count).IsGreaterThan(0);
        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Severity == MetanoDiagnosticSeverity.Warning
                    && d.Code == DiagnosticCodes.UnsupportedFeature
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task SupportedCode_NoDiagnostics()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record Money(int Cents, string Currency);
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task Diagnostic_HasSourceLocation()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Foo
            {
                public void Bar()
                {
                    lock (this) { }
                }
            }
            """
        );

        var diag = diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.UnsupportedFeature);
        await Assert.That(diag).IsNotNull();
        await Assert.That(diag!.Location).IsNotNull();
    }

    [Test]
    public async Task UncoveredOverloadedConstructor_ReportsDiagnosticInsteadOfCrash()
    {
        // The IR pipeline owns multi-constructor dispatch. When one body has
        // a shape the probe rejects (lock statement here), the bridge can't
        // lower the dispatcher — the build must report it instead of either
        // throwing (compiler crash) or silently dropping the type.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Counter
            {
                private readonly object _gate = new();

                public Counter() { }

                public Counter(int initial)
                {
                    lock (_gate) { }
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Severity == MetanoDiagnosticSeverity.Error
                    && d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("Counter")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task UncoveredOverloadedMethod_ReportsDiagnosticInsteadOfCrash()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Counter
            {
                private readonly object _gate = new();

                public void Increment() { }

                public void Increment(int by)
                {
                    lock (_gate) { }
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Severity == MetanoDiagnosticSeverity.Error
                    && d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("Increment")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task UncoveredModuleFunction_ReportsDiagnosticInsteadOfSilentDrop()
    {
        // [ExportedAsModule] static class — when one of its public methods
        // has an uncovered body, TypeTransformer must surface the gap as a
        // diagnostic instead of producing zero output for the type.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, ExportedAsModule]
            public static class Helpers
            {
                private static readonly object _gate = new();

                public static void Run()
                {
                    lock (_gate) { }
                }
            }
            """
        );

        await Assert
            .That(
                diagnostics.Any(d =>
                    d.Severity == MetanoDiagnosticSeverity.Error
                    && d.Code == DiagnosticCodes.UnsupportedFeature
                    && d.Message.Contains("Helpers")
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Format_RoslynStyle()
    {
        var diag = new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Warning,
            "MS0001",
            "Test message",
            null
        );

        await Assert.That(diag.Format()).IsEqualTo("warning MS0001: Test message");
    }
}
