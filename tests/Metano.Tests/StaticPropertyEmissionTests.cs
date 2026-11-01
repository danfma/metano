namespace Metano.Tests;

/// <summary>
/// Static properties keep <c>static</c> on every member they lower to (field,
/// getter, setter), so <c>Type.Prop</c> reads the class and not an instance (#314).
/// </summary>
public class StaticPropertyEmissionTests
{
    [Test]
    public async Task StaticProperties_EmitStaticMembers()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Settings
            {
                private static int _level = 1;

                public static int Count { get; } = 3;
                public static string Title { get; set; } = "x";
                public static int Plain { get; set; }
                public static int Level
                {
                    get => _level;
                    set => _level = value < 0 ? 0 : value;
                }
                public static int Twice => Count * 2;
                public int Instance { get; } = 7;
            }
            """
        );

        var expected = TranspileHelper.ReadExpected("static-properties.ts");
        await Assert.That(result["settings.ts"]).IsEqualTo(expected);
    }
}
