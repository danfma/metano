namespace Metano.Tests;

/// <summary>
/// A bound call into an overloaded containerless group emits the overload's fast path
/// (<c>twiceS(...)</c>, spec 013 slice 013e), so a parameter spelled like that fast path
/// shadows it as a parameter spelled like the function does: it takes the <c>$</c>
/// suffix on both targets (#578).
/// </summary>
public class FastPathParameterShadowingTests
{
    private const string Source = """
        [Transpile, NoContainer]
        public static class M
        {
            public static int Twice(int x) => x * 2;
            public static int Twice(string s) => s.Length * 2;
        }

        [Transpile]
        public class Use
        {
            public int Go(string twiceS) => M.Twice(twiceS);
        }
        """;

    [Test]
    public async Task TypeScript_ParameterNamedLikeTheCalledFastPath_IsRenamed()
    {
        var files = TranspileHelper.Transpile(Source);

        var use = files["use.ts"];

        await Assert.That(use).Contains("go(twiceS$: string): number {");
        await Assert.That(use).Contains("return twiceS(twiceS$);");
    }

    [Test]
    public async Task Dart_ParameterNamedLikeTheCalledFastPath_IsRenamed()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        var use = files["use.dart"];

        await Assert.That(use).Contains("String twiceS$");
        await Assert.That(use).Contains("twiceS(twiceS$)");
    }
}
