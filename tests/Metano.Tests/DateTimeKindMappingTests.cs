namespace Metano.Tests;

/// <summary>
/// The <c>DateTime</c> constructors that take a <c>DateTimeKind</c> lower to the kind-less
/// factories, since a <c>Temporal.PlainDateTime</c> carries no kind (#753).
/// </summary>
public class DateTimeKindMappingTests
{
    [Test]
    public async Task DateTimeKindConstructors_LowerToTheKindLessFactories()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Stamps
            {
                public DateTime Seconds() => new DateTime(2024, 3, 5, 10, 20, 30, DateTimeKind.Utc);
                public DateTime Millis() => new DateTime(2024, 3, 5, 10, 20, 30, 40, DateTimeKind.Local);
                public DateTime Ticks(long ticks) => new DateTime(ticks, DateTimeKind.Utc);
            }
            """
        );

        await TranspileHelper.AssertGolden("datetime-kind-constructors.ts", files["stamps.ts"]);
        await Assert.That(files["stamps.ts"]).DoesNotContain("DateTimeKind");
    }
}
