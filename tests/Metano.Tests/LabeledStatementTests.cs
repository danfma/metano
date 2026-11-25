using Metano.Compiler.Diagnostics;
using Metano.Tests.TreeShaking;

namespace Metano.Tests;

/// <summary>
/// C# labeled statements and the labeled <c>break</c> / <c>continue</c> of C# 15
/// (#772, decision W9-12) lower to native JavaScript and Dart labels on the same
/// loop or <c>switch</c>; a <c>goto</c>, which has no equivalent, is an error.
/// </summary>
public class LabeledStatementTests
{
    private const string LoopsSource = """
        [Transpile]
        public static class Jumps
        {
            public static int Scan(int[] a)
            {
                var n = 0;
                top: while (true)
                {
                    while (n < a.Length) { n++; continue top; }
                    break top;
                }
                return n;
            }

            public static int FirstBlockedRow(int[][] grid)
            {
                var found = -1;
                rows: for (var row = 0; row < grid.Length; row++)
                {
                    cols: foreach (var cell in grid[row])
                    {
                        if (cell < 0) continue rows;
                        if (cell == 0) continue cols;
                        if (cell > 9) { found = row; break rows; }
                    }
                }
                return found;
            }

            public static int Countdown(int n)
            {
                var steps = 0;
                outer: do
                {
                    for (var i = 0; i < 3; i++)
                    {
                        steps++;
                        if (steps > 10) break outer;
                    }
                    n--;
                } while (n > 0);
                return steps;
            }

            public static string Dispatch(int kind, int[] steps)
            {
                var log = "";
                dispatch: switch (kind)
                {
                    case 1:
                        foreach (var step in steps)
                        {
                            if (step < 0) break dispatch;
                            log += step;
                        }
                        log += "!";
                        break;
                    default:
                        log = "default";
                        break;
                }
                return log;
            }
        }
        """;

    [Test]
    public async Task LabeledLoops_EmitNativeLabels()
    {
        var (output, diagnostics) = TranspileHelper.TranspileWithDiagnostics(LoopsSource);
        var jumps = output["jumps.ts"];

        await Assert.That(diagnostics).IsEmpty();
        await TranspileHelper.AssertGolden("labeled-statements.ts", jumps);
        await Assert.That(jumps).Contains("top: while (true) {");
        await Assert.That(jumps).Contains("continue top;");
        await Assert.That(jumps).Contains("break top;");
        await Assert.That(jumps).Contains("rows: for (let row = 0;");
        await Assert.That(jumps).Contains("cols: for (const cell of ");
        await Assert.That(jumps).Contains("outer: do {");
        await Assert.That(jumps).Contains("dispatch: switch (kind) {");
        await Assert.That(jumps).Contains("break dispatch;");
    }

    [Test]
    public async Task LabeledLoops_TypeCheckUnderStrict()
    {
        var output = TranspileHelper.Transpile(LoopsSource);

        var (exitCode, log) = await StrictTypeCheck.Run(new Dictionary<string, string>(), output);

        await Assert.That(log).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task LabeledContinue_FromANestedLoop_KeepsTheIncrementOfAMovedForStep()
    {
        // The `for` header's increment reads a local its condition declares,
        // so the increment moves into the body unless a `continue` would
        // skip it there: a labeled `continue rows` from the nested loop does.
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Grid
            {
                public static bool Has(int row, out int width) { width = row; return row < 3; }

                public static int Walk()
                {
                    var total = 0;
                    rows: for (var row = 0; Has(row, out var width); row += width + 1)
                    {
                        for (var i = 0; i < 2; i++)
                        {
                            total++;
                            if (i == 1) continue rows;
                        }
                    }
                    return total;
                }
            }
            """
        );

        var grid = output["grid.ts"];

        await Assert.That(grid).Contains("rows: ");
        await Assert.That(grid).Contains("continue rows;");
    }

    [Test]
    public async Task Goto_ReportsAnError()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public static class Jump
            {
                public static int Loop(int n)
                {
                    again: n--;
                    if (n > 0) goto again;
                    return n;
                }
            }
            """
        );

        var error = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedFeature);

        await Assert.That(error.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(error.Message).Contains("'goto' is not supported");
    }

    [Test]
    public async Task Dart_LabeledLoops_EmitNativeLabels()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public static class Jumps
            {
                public static int Scan(int[] a)
                {
                    var n = 0;
                    top: while (true)
                    {
                        while (n < a.Length) { n++; continue top; }
                        break top;
                    }
                    return n;
                }
            }
            """
        );

        var jumps = files["jumps.dart"];

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(jumps).Contains("top: while (true) {");
        await Assert.That(jumps).Contains("continue top;");
        await Assert.That(jumps).Contains("break top;");
    }
}
