using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

public class DiagnosticsTests
{
    [Test]
    public async Task UnsupportedStatement_ReportsAnErrorAtTheStatement()
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

        // W9-13: the placeholder drops the statement, so the output is known
        // to be wrong — an error, at the statement itself.
        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);
        var reported = error.Location!.SourceTree!.GetText().ToString(error.Location.SourceSpan);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(error.Message).Contains("The C# lock statement");
        await Assert.That(reported).StartsWith("lock (_gate)");
    }

    [Test]
    public async Task ThrowingExpressionPlaceholder_KeepsTheWarning()
    {
        // The expression placeholder throws when reached: it fails loudly, so
        // it stays an MS0001 warning (W9-13).
        var (output, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Calc
            {
                public static int Twice(int a)
                {
                    int Inner(int x) => x * 2;
                    return Inner(a);
                }
            }
            """
        );

        var severities = diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnsupportedFeature)
            .Select(d => d.Severity);

        await Assert.That(severities).Contains(MetanoDiagnosticSeverity.Warning);
        await Assert.That(output["calc.ts"]).Contains("throw new Error(\"Unsupported expression");
    }

    [Test]
    public async Task EmptyStatement_IsDroppedWithoutDiagnostics()
    {
        var (output, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Scan
            {
                public static int Skip(int[] xs)
                {
                    var i = 0;
                    while (i < xs.Length && xs[i++] == 0) ;
                    ;
                    return i;
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(output["scan.ts"]).DoesNotContain("TODO");
    }

    [Test]
    public async Task DartPlaceholder_ReportsAWarningAtTheCSharpConstruct()
    {
        // The Dart body printer has no `for` loop yet: its placeholder drops
        // the loop. The Dart target is a prototype, so it is a warning
        // (W9-13a) that names the C# construct and points at it.
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Loop
            {
                public static int Sum(int n)
                {
                    var total = 0;
                    for (var i = 0; i < n; i++) total += i;
                    return total;
                }
            }
            """
        );

        var warning = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);
        var position = warning.Location!.GetLineSpan().StartLinePosition;

        var expected = "The C# for statement is not supported by the Dart target.";

        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(warning.Message).IsEqualTo(expected);
        await Assert.That(position.Character).IsEqualTo(8);
        await Assert.That(string.Concat(files.Values)).Contains("TODO");
    }

    [Test]
    public async Task UsingDeclaration_ReportsTheDroppedDispose()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public sealed class Resource : IDisposable
            {
                public void Dispose() { }
            }

            [Transpile]
            public static class Uses
            {
                public static int Run()
                {
                    using var resource = new Resource();
                    return 1;
                }
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(error.Message).Contains("using declaration");
    }

    [Test]
    public async Task UnusedLocalFunction_ReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Uses
            {
                public static int Run(int a)
                {
                    int Unused(int x) => x * 2;
                    return a;
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
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
        // [NoContainer] static class — when one of its public methods
        // has an uncovered body, TypeTransformer must surface the gap as a
        // diagnostic instead of producing zero output for the type.
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, NoContainer]
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
