namespace Metano.Tests;

/// <summary>
/// <c>default</c> of a tuple type is the tuple of its element defaults
/// (#403, #415), so <c>t.Equals(default)</c> and
/// <c>nullable.GetValueOrDefault()</c> compare against <c>(0, 0)</c>
/// instead of <c>undefined</c>.
/// </summary>
public class TupleDefaultValueTests
{
    private const string Source = """
        [Transpile]
        public static class Defaults
        {
            public static bool EqualsDefault((int, int) t) => t.Equals(default);

            public static bool ValueOrDefault((int, int)? t) => t.GetValueOrDefault().Equals((0, 0));

            public static (int, string) Local()
            {
                (int, string) t = default;

                return t;
            }
        }
        """;

    [Test]
    public async Task DefaultTuple_IsTheTupleOfElementDefaults()
    {
        var output = TranspileHelper.Transpile(Source)["defaults.ts"];

        await Assert.That(output).Contains("tupleEquals(t, [0, 0] as [number, number])");
        await Assert.That(output).Contains("const t = [0, null] as [number, string];");
    }

    [Test]
    public async Task GetValueOrDefault_FallsBackToTheDefaultTuple()
    {
        var output = TranspileHelper.Transpile(Source)["defaults.ts"];

        await Assert
            .That(output)
            .Contains("tupleEquals(t ?? [0, 0] as [number, number], [0, 0] as [number, number])");
    }
}
