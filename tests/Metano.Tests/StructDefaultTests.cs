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

    private const string ParameterDefaultSource = """
        [Transpile]
        public struct Vec
        {
            public int X;
            public int Y;

            public Vec(int x, int y) { X = x; Y = y; }
        }

        [Transpile]
        public static class Defaults
        {
            public static int TakeV(Vec v = default) => v.X;
        }
        """;

    [Test]
    public async Task ParameterDefaultOfAStructWithAConstructor_IsTheZeroValueFactory()
    {
        var output = TranspileHelper.Transpile(ParameterDefaultSource)["defaults.ts"];

        await Assert.That(output).Contains("v: Vec = Vec.$default()");
    }

    [Test]
    public async Task Dart_ParameterDefaultOfAStruct_KeepsThePlaceholderAndItsWarning()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(ParameterDefaultSource);
        var output = string.Join("\n", files.Values);
        var warned = diagnostics.Any(d => d.Message.Contains("'default(Vec)' has no literal"));

        await Assert.That(output).DoesNotContain("Vec v = Vec()");
        await Assert.That(output).Contains("/* default */");
        await Assert.That(warned).IsTrue();
    }
}
