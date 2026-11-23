namespace Metano.Tests;

/// <summary>
/// A switch-section constant read by another section is declared ahead of the
/// switch with its initializer; the constants that initializer reads move with
/// it, transitively and in source order, so the hoisted declaration never reads
/// a name still scoped to its section (#391).
/// </summary>
public class SwitchHoistedConstantTests
{
    [Test]
    public async Task ConstantsAHoistedConstantReads_AreHoistedInSourceOrder()
    {
        var output = TranspileHelper.Transpile(
            """
            [Transpile]
            public abstract record Shape;

            [Transpile]
            public sealed record Circle(int R) : Shape;

            [Transpile]
            public sealed record Square(int S) : Shape;

            [Transpile]
            public static class Measurer
            {
                public static int Measure(Shape s)
                {
                    switch (s)
                    {
                        case Circle c:
                            const int a = 1;
                            const int k = a + 1;
                            const int unrelated = 9;
                            const int j = k + 1;
                            return c.R * j + unrelated;
                        case Square q:
                            return q.S + j;
                        default:
                            return 0;
                    }
                }
            }
            """
        )["measurer.ts"];

        await Assert
            .That(output)
            .Contains(
                """
                      const a = 1;
                      const k = a + 1;
                      const j = k + 1;

                      switch (true) {
                """
            );

        await Assert.That(output).Contains("const unrelated = 9;");
        await Assert.That(output.Split("const k = a + 1;").Length).IsEqualTo(2);
    }
}
