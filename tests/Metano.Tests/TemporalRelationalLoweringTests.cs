namespace Metano.Tests;

/// <summary>
/// Tests covering the Temporal relational-operator lowering fix for
/// issue #90. Relational operators (<c>&gt; &gt;= &lt; &lt;=</c>)
/// applied to Temporal-backed BCL types (<c>DateTime</c>,
/// <c>DateTimeOffset</c>, <c>DateOnly</c>, <c>TimeOnly</c>,
/// <c>TimeSpan</c>) emit
/// <c>Temporal.Type.compare(a, b) op 0</c> instead of raw
/// <c>a op b</c> — the built-in operators throw a TypeError on
/// Temporal instances at runtime.
/// </summary>
public class TemporalRelationalLoweringTests
{
    [Test]
    public async Task DateOnly_GreaterThanOrEqual_LowersThroughCompare()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Scheduler
            {
                public bool OnOrAfter(System.DateOnly a, System.DateOnly b) => a >= b;
            }
            """
        );

        var output = result["scheduler.ts"];
        await Assert.That(output).Contains("Temporal.PlainDate.compare(a, b) >= 0");
        await Assert.That(output).DoesNotContain("a >= b");
    }

    [Test]
    public async Task DateOnly_LessThan_LowersThroughCompare()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Scheduler
            {
                public bool Before(System.DateOnly a, System.DateOnly b) => a < b;
            }
            """
        );

        var output = result["scheduler.ts"];
        await Assert.That(output).Contains("Temporal.PlainDate.compare(a, b) < 0");
    }

    [Test]
    public async Task DateTime_Range_LowersThroughCompare()
    {
        // Chained range check (the original failure mode from
        // sample-issue-tracker's Sprint.IsActiveOn).
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Window
            {
                public bool Covers(System.DateTime at, System.DateTime start, System.DateTime end) =>
                    at >= start && at <= end;
            }
            """
        );

        var output = result["window.ts"];
        await Assert.That(output).Contains("Temporal.PlainDateTime.compare(at, start) >= 0");
        await Assert.That(output).Contains("Temporal.PlainDateTime.compare(at, end) <= 0");
    }

    [Test]
    public async Task DateTimeOffset_Compare_UsesZonedDateTime()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Clock
            {
                public bool After(System.DateTimeOffset a, System.DateTimeOffset b) => a > b;
            }
            """
        );

        var output = result["clock.ts"];
        await Assert.That(output).Contains("Temporal.ZonedDateTime.compare(a, b) > 0");
    }

    [Test]
    public async Task TimeOnly_Compare_UsesPlainTime()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Shift
            {
                public bool Earlier(System.TimeOnly a, System.TimeOnly b) => a <= b;
            }
            """
        );

        var output = result["shift.ts"];
        await Assert.That(output).Contains("Temporal.PlainTime.compare(a, b) <= 0");
    }

    [Test]
    public async Task TimeSpan_Compare_UsesDuration()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class Timer
            {
                public bool Longer(System.TimeSpan a, System.TimeSpan b) => a > b;
            }
            """
        );

        var output = result["timer.ts"];
        await Assert.That(output).Contains("Temporal.Duration.compare(a, b) > 0");
    }

    [Test]
    public async Task Int_Compare_DoesNotTriggerTemporalLowering()
    {
        // Regression guard: non-Temporal types keep their raw
        // relational lowering so the fix does not over-reach.
        var result = TranspileHelper.Transpile(
            """
            [assembly: TranspileAssembly]

            public class IntCompare
            {
                public bool Over(int a, int b) => a >= b;
            }
            """
        );

        var output = result["int-compare.ts"];
        await Assert.That(output).Contains("return a >= b;");
        await Assert.That(output).DoesNotContain("compare");
    }
}
