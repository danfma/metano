namespace Metano.Tests;

/// <summary>
/// A temporal value tests inline by its <c>Object.prototype.toString</c> tag,
/// the predicate its descriptor's <c>test</c> uses (spec 011 FR-017): never by
/// <c>instanceof Temporal.*</c>, which pulls the polyfill in, and never through
/// <c>isType</c>, which pulls reflection into a dispatcher or guard that asks no
/// runtime question (NFR-010).
/// </summary>
public class RuntimeTypeTemporalTestTests
{
    private const string Source = """
        using System;
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Clock;

        [GenerateGuard]
        public record Stamp(DateTimeOffset At);

        public static class Probe
        {
            public static string Over(string text, DateTimeOffset at) => text;

            public static string Over(string text, int count) => text;
        }
        """;

    [Test]
    public async Task ATemporalOverloadDispatcher_ImportsNoReflectionFunction()
    {
        var probe = TranspileHelper.Transpile(Source)["clock/probe.ts"];

        await Assert
            .That(probe)
            .Contains(
                "Object.prototype.toString.call(args[1]) === \"[object Temporal.ZonedDateTime]\""
            );

        await Assert.That(probe).DoesNotContain("isType");
        await Assert.That(probe).DoesNotContain("DateTimeOffsetType");
        await Assert.That(probe).DoesNotContain("instanceof Temporal");
    }

    [Test]
    public async Task ATemporalGuardField_TestsTheTagInline()
    {
        var stamp = TranspileHelper.Transpile(Source)["clock/stamp.ts"];

        await Assert
            .That(stamp)
            .Contains(
                "Object.prototype.toString.call(v.at) === \"[object Temporal.ZonedDateTime]\""
            );

        await Assert.That(stamp).DoesNotContain("isType");
    }
}
