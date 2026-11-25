namespace Metano.Tests;

/// <summary>
/// A target-typed <c>default</c> / <c>default(S)</c> of a struct that declares a
/// constructor reads the struct's zero-value factory in every position, where it
/// printed <c>undefined</c> (#802). A type parameter keeps its own handling.
/// </summary>
public class StructDefaultTests
{
    [Test]
    public async Task DefaultOfAStructWithAConstructor_IsTheZeroValueFactory_InEveryPosition()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public struct Pt
            {
                public int X;
                public int Y;

                public Pt(int x, int y) { X = x; Y = y; }
            }

            [Transpile]
            public static class Use
            {
                public static Pt Returned() => default;
                public static Pt Explicit() => default(Pt);
                public static void Take(Pt p) { }

                public static void Positions(bool flag)
                {
                    Take(default);
                    Pt local = default;
                    local = default;
                    var picked = flag ? default(Pt) : new Pt(1, 2);
                }
            }
            """
        );

        var output = files["use.ts"];

        await Assert.That(output).Contains("return Pt.$default();");
        await Assert.That(output).Contains("Use.take(Pt.$default());");
        await Assert.That(output).Contains("let local = Pt.$default();");
        await Assert.That(output).Contains("local = Pt.$default();");
        await Assert.That(output).Contains("flag ? Pt.$default() : new Pt(1, 2)");
        await Assert.That(output).DoesNotContain("undefined");
        await Assert.That(files["pt.ts"]).Contains("static $default(): Pt");
    }
}
