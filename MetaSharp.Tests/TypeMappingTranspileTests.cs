namespace MetaSharp.Tests;

public class TypeMappingTranspileTests
{
    // ─── Temporal types ─────────────────────────────────────

    [Test]
    public async Task DateTime_MapsToTemporalPlainDateTime()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Event(string Name, DateTime StartedAt);
            """
        );

        var output = result["Event.ts"];
        await Assert.That(output).Contains("startedAt: Temporal.PlainDateTime");
        await Assert.That(output).Contains("from \"@js-temporal/polyfill\"");
    }

    [Test]
    public async Task DateOnly_MapsToTemporalPlainDate()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Birthday(string Name, DateOnly Date);
            """
        );

        var output = result["Birthday.ts"];
        await Assert.That(output).Contains("date: Temporal.PlainDate");
    }

    [Test]
    public async Task TimeOnly_MapsToTemporalPlainTime()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Alarm(TimeOnly Time);
            """
        );

        var output = result["Alarm.ts"];
        await Assert.That(output).Contains("time: Temporal.PlainTime");
    }

    [Test]
    public async Task DateTimeOffset_MapsToTemporalZonedDateTime()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Log(DateTimeOffset Timestamp);
            """
        );

        var output = result["Log.ts"];
        await Assert.That(output).Contains("timestamp: Temporal.ZonedDateTime");
    }

    [Test]
    public async Task TimeSpan_MapsToTemporalDuration()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Timer(TimeSpan Elapsed);
            """
        );

        var output = result["Timer.ts"];
        await Assert.That(output).Contains("elapsed: Temporal.Duration");
    }

    [Test]
    public async Task TemporalImport_OnlyAddedOnce()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Schedule(DateTime Start, DateTime End, TimeSpan Duration);
            """
        );

        var output = result["Schedule.ts"];
        var count = output.Split("@js-temporal/polyfill").Length - 1;
        await Assert.That(count).IsEqualTo(1);
    }

    // ─── Simple type mappings ───────────────────────────────

    [Test]
    public async Task Guid_MapsToString()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Entity(Guid Id, string Name);
            """
        );

        var output = result["Entity.ts"];
        await Assert.That(output).Contains("id: string");
    }

    // ─── Dictionary → Map ───────────────────────────────────

    [Test]
    public async Task Dictionary_MapsToMap()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Config(System.Collections.Generic.Dictionary<string, int> Settings);
            """
        );

        var output = result["Config.ts"];
        await Assert.That(output).Contains("settings: Map<string, number>");
    }

    [Test]
    public async Task IReadOnlyDictionary_MapsToMap()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Index(System.Collections.Generic.IReadOnlyDictionary<string, string> Data);
            """
        );

        var output = result["Index.ts"];
        await Assert.That(output).Contains("data: Map<string, string>");
    }

    // ─── HashSet → Set ──────────────────────────────────────

    [Test]
    public async Task HashSet_MapsToSet()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Tags(System.Collections.Generic.HashSet<string> Items);
            """
        );

        var output = result["Tags.ts"];
        await Assert.That(output).Contains("items: HashSet<string>");
    }

    // ─── Tuple → [T1, T2] ──────────────────────────────────

    [Test]
    public async Task ValueTuple_MapsToTsTuple()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Pair(ValueTuple<int, string> Value);
            """
        );

        var output = result["Pair.ts"];
        await Assert.That(output).Contains("value: [number, string]");
    }

    [Test]
    public async Task Tuple_MapsToTsTuple()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Triple(Tuple<int, string, bool> Value);
            """
        );

        var output = result["Triple.ts"];
        await Assert.That(output).Contains("value: [number, string, boolean]");
    }

    // ─── No unnecessary imports for global types ────────────

    [Test]
    public async Task MapAndSet_NoExtraImportNeeded()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Data(
                System.Collections.Generic.Dictionary<string, int> Map,
                System.Collections.Generic.HashSet<string> Set
            );
            """
        );

        var output = result["Data.ts"];
        // Map and Set are global — no import should be generated for them
        // (HashCode import is expected for records)
        await Assert.That(output).DoesNotContain("from \"Map\"");
        await Assert.That(output).DoesNotContain("from \"Set\"");
        await Assert.That(output).DoesNotContain("@js-temporal");
    }
}
