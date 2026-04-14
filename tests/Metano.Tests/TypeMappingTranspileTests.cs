namespace Metano.Tests;

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

        var output = result["event.ts"];
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

        var output = result["birthday.ts"];
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

        var output = result["alarm.ts"];
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

        var output = result["log.ts"];
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

        var output = result["timer.ts"];
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

        var output = result["schedule.ts"];
        var count = output.Split("@js-temporal/polyfill").Length - 1;
        await Assert.That(count).IsEqualTo(1);
    }

    // ─── Simple type mappings ───────────────────────────────

    [Test]
    public async Task Guid_MapsToUuidBrandedType()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Entity(Guid Id, string Name);
            """
        );

        var output = result["entity.ts"];
        // Guid → branded UUID type from metano-runtime (not plain string).
        await Assert.That(output).Contains("id: UUID");
        // UUID may be merged with other metano-runtime imports (e.g., HashCode)
        await Assert.That(output).Contains("UUID");
        await Assert.That(output).Contains("from \"metano-runtime\"");
    }

    [Test]
    public async Task GuidNewGuid_LowersToUuidNewUuid()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class IdFactory
            {
                public static Guid Create() => Guid.NewGuid();
            }
            """
        );

        var output = result["id-factory.ts"];
        await Assert.That(output).Contains("UUID.newUuid()");
        // UUID may be merged with other metano-runtime imports (e.g., HashCode)
        await Assert.That(output).Contains("UUID");
        await Assert.That(output).Contains("from \"metano-runtime\"");
    }

    [Test]
    public async Task GuidToStringN_LowersToHyphenStrip()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class IdFactory
            {
                public static string Compact(Guid id) => id.ToString("N");
            }
            """
        );

        var output = result["id-factory.ts"];
        // The "N" form strips hyphens via String.replace.
        await Assert.That(output).Contains("replace(/-/g, \"\")");
    }

    [Test]
    public async Task GuidEmpty_LowersToUuidEmpty()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class IdFactory
            {
                public static Guid GetEmpty() => Guid.Empty;
            }
            """
        );

        var output = result["id-factory.ts"];
        await Assert.That(output).Contains("UUID.empty");
        // UUID may be merged with other metano-runtime imports (e.g., HashCode)
        await Assert.That(output).Contains("UUID");
        await Assert.That(output).Contains("from \"metano-runtime\"");
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

        var output = result["config.ts"];
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

        var output = result["index.ts"];
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

        var output = result["tags.ts"];
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

        var output = result["pair.ts"];
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

        var output = result["triple.ts"];
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

        var output = result["data.ts"];
        // Map and Set are global — no import should be generated for them
        // (HashCode import is expected for records)
        await Assert.That(output).DoesNotContain("from \"Map\"");
        await Assert.That(output).DoesNotContain("from \"Set\"");
        await Assert.That(output).DoesNotContain("@js-temporal");
    }
}
