using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The BCL constructors and parsers whose JavaScript lowering used to build a wrong value or
/// none at all (baseline FR-054, #673): <c>TimeSpan</c> components and factories,
/// <c>DateTimeOffset</c> components, <c>Guid.Parse</c> and <c>decimal.TryParse</c>. The runtime
/// helpers they call are checked against real .NET by the rows of <c>tests/BclReference</c>.
/// </summary>
public class BclConstructionMappingTests
{
    [Test]
    public async Task TimeSpanComponents_LowerToTheComponentSumHelper()
    {
        var output = TranspileText(
            """
            public static TimeSpan Minutes() => new TimeSpan(0, 5, 0);
            public static TimeSpan Days() => new TimeSpan(1, 2, 3, 4);
            public static TimeSpan Millis() => new TimeSpan(1, 2, 3, 4, 5);
            public static TimeSpan Micros() => new TimeSpan(1, 2, 3, 4, 5, 6);
            public static TimeSpan Zero() => TimeSpan.Zero;
            public static TimeSpan Ticks(long t) => new TimeSpan(t);
            """
        );

        await Assert.That(output).Contains("return timeSpanFromParts(0, 0, 5, 0);");
        await Assert.That(output).Contains("return timeSpanFromParts(1, 2, 3, 4);");
        await Assert.That(output).Contains("return timeSpanFromParts(1, 2, 3, 4, 5);");
        await Assert.That(output).Contains("return timeSpanFromParts(1, 2, 3, 4, 5, 6);");
        await Assert.That(output).Contains("return timeSpanZero;");
        await Assert.That(output).Contains("return durationFromTicks(t);");
        await Assert.That(output).DoesNotContain("new Temporal.Duration(0, 5, 0)");
    }

    [Test]
    public async Task TimeSpanFactories_LowerToTheUnitHelpers()
    {
        var output = TranspileText(
            """
            public static TimeSpan A(double d) => TimeSpan.FromDays(d) + TimeSpan.FromHours(d);
            public static TimeSpan B(double d) => TimeSpan.FromMinutes(d) + TimeSpan.FromSeconds(d);
            public static TimeSpan C(double d) => TimeSpan.FromMilliseconds(d) + TimeSpan.FromMicroseconds(d);
            """
        );

        await Assert.That(output).Contains("timeSpanFromDays(d)");
        await Assert.That(output).Contains("timeSpanFromHours(d)");
        await Assert.That(output).Contains("timeSpanFromMinutes(d)");
        await Assert.That(output).Contains("timeSpanFromSeconds(d)");
        await Assert.That(output).Contains("timeSpanFromMilliseconds(d)");
        await Assert.That(output).Contains("timeSpanFromMicroseconds(d)");
    }

    [Test]
    public async Task DateTimeOffsetComponents_LowerToTheOffsetHelpers()
    {
        var output = TranspileText(
            """
            public static DateTimeOffset A(TimeSpan o) => new DateTimeOffset(2024, 1, 2, 3, 4, 5, o);
            public static DateTimeOffset B(TimeSpan o) => new DateTimeOffset(2024, 1, 2, 3, 4, 5, 6, o);
            public static DateTimeOffset C(DateTime d, TimeSpan o) => new DateTimeOffset(d, o);
            public static DateTimeOffset D(long t, TimeSpan o) => new DateTimeOffset(t, o);
            """
        );

        await Assert.That(output).Contains("return dateTimeOffset(2024, 1, 2, 3, 4, 5, 0, o);");
        await Assert.That(output).Contains("return dateTimeOffset(2024, 1, 2, 3, 4, 5, 6, o);");
        await Assert.That(output).Contains("return dateTimeOffsetFromDateTime(d, o);");
        await Assert.That(output).Contains("return dateTimeOffsetFromTicks(t, o);");
    }

    [Test]
    public async Task GuidParse_NormalizesThroughUuidParse()
    {
        var output = TranspileText(
            """
            public static Guid Parse(string s) => Guid.Parse(s);
            """
        );

        await Assert.That(output).Contains("return UUID.parse(s);");
        await Assert.That(output).DoesNotContain("UUID.create(s)");
    }

    [Test]
    public async Task DecimalTryParse_PassesTheDecimalClass()
    {
        var output = TranspileText(
            """
            public static decimal Parse(string s) => decimal.TryParse(s, out var d) ? d : -1m;
            """
        );

        await Assert.That(output).Contains("tryParseDecimal(s, Decimal)");
    }

    private static string TranspileText(string members) =>
        TranspileHelper.Transpile(Source(members))["text.ts"];

    private static string Source(string members) =>
        $$"""
            using System.Globalization;

            [Transpile]
            public static class Text
            {
            {{members}}
            }
            """;
}
