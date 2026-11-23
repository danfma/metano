namespace Metano.Tests;

/// <summary>
/// Delegate conversions and invocations: a lambda converted to a <c>params</c>
/// delegate (#338), a delegate's own <c>Invoke</c> used as a method group
/// (#375), and a <c>[This]</c> delegate invoked with a positional receiver and
/// named arguments (#376).
/// </summary>
public class DelegateConversionTests
{
    private const string Delegates = """
        public delegate int Folder(params int[] xs);
        public delegate int SeededFolder(int seed, params int[]? xs);
        public delegate int Listener([This] Holder self, int x);

        [Transpile]
        public class Holder
        {
            public int V = 1;
            public System.Action<Holder, int> Prop { get; set; } = (h, x) => { };
            public System.Func<Holder, int, int> F { get; set; } = (h, x) => h.V + x;
            public Listener L { get; set; } = (self, x) => self.V + x;
        }
        """;

    [Test]
    public async Task LambdaToParamsDelegate_DeclaresARestParameter()
    {
        var output = TranspileProbe(
            """
            public static int A() { Folder f = xs => xs.Length; return f(1, 2, 3); }
            public static int B() { SeededFolder f = (s, xs) => s + (xs?.Length ?? 0); return f(10, 1, 2); }
            public static int C(Folder f) => f(1, 2);
            public static int D() => C(xs => xs.Length);
            public static int E() { System.Func<int[], int> f = xs => xs.Length; return f([1]); }
            """
        );

        await Assert.That(output).Contains("const f = (...xs: number[]) => xs.length;");
        await Assert.That(output).Contains("(s: number, ...xs: number[]) =>");
        await Assert.That(output).Contains("return Probe.c((...xs: number[]) => xs.length);");
        await Assert.That(output).Contains("const f = (xs: number[]) => xs.length;");
    }

    [Test]
    public async Task DelegateInvokeMethodGroup_ConvertsTheDelegateItself()
    {
        var output = TranspileProbe(
            """
            public static void A(Holder h) { System.Action<Holder, int> f = h.Prop.Invoke; f(h, 1); }
            public static int B(Holder h) { Listener g = h.F.Invoke; return g(h, 2); }
            public static int C(Holder h) { Listener g = h.L.Invoke; return g(h, 2); }
            """
        );

        await Assert.That(output).Contains("const f = h.prop;");
        await Assert.That(output).Contains("const g = bindReceiver(h.f);");
        await Assert.That(output).Contains("const g = h.l;");
        await Assert.That(output).DoesNotContain(".invoke");
    }

    [Test]
    public async Task ThisDelegateInvokeToPlainDelegate_ReportsUnsupported()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Wrap(
                "public static int A(Holder h) { System.Func<Holder, int, int> g = h.L.Invoke; return g(h, 3); }"
            )
        );

        await Assert
            .That(diagnostics)
            .Contains(diagnostic =>
                diagnostic.Code == Metano.Compiler.Diagnostics.DiagnosticCodes.UnsupportedFeature
                && diagnostic.Message.Contains("h.L.Invoke")
            );
    }

    [Test]
    public async Task ThisDelegate_PositionalReceiverWithNamedArguments_KeepsTheReceiver()
    {
        var output = TranspileProbe(
            """
            public static int A(Holder h, Listener a) => a.Invoke(h, x: 20);
            public static int B(Holder h, Listener a) => a(h, x: 20);
            public static int C(Holder h, Listener a) => a.Invoke(x: 21, self: h);
            """
        );

        await Assert.That(output).Contains("return a.call(h, 20);");
        await Assert.That(output).Contains("return a.call(h, 21);");
        await Assert.That(output).DoesNotContain("return a(h, 20);");
    }

    private static string TranspileProbe(string members) =>
        TranspileHelper.Transpile(Wrap(members))["probe.ts"];

    private static string Wrap(string members) =>
        Delegates
        + $$"""

            [Transpile]
            public static class Probe
            {
            {{members}}
            }
            """;
}
