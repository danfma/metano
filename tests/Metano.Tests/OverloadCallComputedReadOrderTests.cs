namespace Metano.Tests;

/// <summary>
/// A call bound to an overload group whose named arguments are out of
/// parameter order keeps C#'s evaluation order when an argument runs user
/// code its lowered shape does not show (spec 013 FR-017, NFR-004, #592): a
/// computed property read or a concatenation that formats an operand
/// through its <c>ToString</c>. The TypeScript target binds the arguments
/// in source order as soon as a second argument is order-sensitive. Both
/// goldens compile under <c>tsgo --strict --noImplicitOverride</c>; the
/// same source run under .NET, Bun and Node prints the same trace for
/// every call.
/// </summary>
public class OverloadCallComputedReadOrderTests
{
    private const string Source = """
        [Transpile]
        public static class Trace
        {
            public static string Log = "";

            public static int Count;

            public static int Next(string tag)
            {
                Log += tag;
                Count = Count + 1;

                return Count;
            }

            public static string Take(int value)
            {
                var line = Log + ":" + value;
                Log = "";
                Count = 0;

                return line;
            }
        }

        [Transpile]
        public interface IReading
        {
            int Value { get; }
        }

        [Transpile]
        public class Reading : IReading
        {
            public int Value => Trace.Next("v");
        }

        [Transpile]
        public class Plain
        {
            public virtual int Slot { get; set; }
        }

        [Transpile]
        public record Point(int X, int Y);

        [Transpile]
        public class Label
        {
            public string Text = "";

            public override string ToString() => Text + Trace.Next(Text);
        }

        [Transpile]
        public interface IEngine
        {
            int Apply(int f, int d);

            int Apply(string mode);
        }

        [Transpile]
        public class Engine : IEngine
        {
            public int Total;

            public Engine() { }

            public Engine(int f, int d) { Total = f * 10 + d; }

            public Engine(string mode) { Total = mode.Length; }

            public int Apply(int f, int d) => f * 10 + d;

            public int Apply(string mode) => mode.Length;

            public static int Mix(int f, int d) => f * 10 + d;

            public static int Mix(string mode) => mode.Length;

            public int Join(string f, string d) => f.Length * 10 + d.Length;

            public int Join(int mode) => mode;
        }

        [Transpile]
        public static class EngineExtensions
        {
            public static int Scale(this Engine e, int f, int d) => f * 10 + d;

            public static int Scale(this Engine e, string mode) => mode.Length;
        }

        [Transpile, NoContainer]
        public static class Mixer
        {
            public static int Blend(int f, int d) => f * 10 + d;

            public static int Blend(string mode) => mode.Length;
        }

        [Transpile]
        public class Holder
        {
            public int Field;

            public int Auto { get; set; }

            public static int Shared { get; set; }

            public int P => Trace.Next("p");

            public int Q => Trace.Next("q");

            public static int S => Trace.Next("s");

            public int Bump
            {
                get
                {
                    Auto = Auto + 1;

                    return Auto;
                }
            }

            public int BumpField
            {
                get
                {
                    Field = Field + 1;

                    return Field;
                }
            }

            public Engine Target
            {
                get
                {
                    Trace.Next("t");

                    return new Engine();
                }
            }

            public Label First = new Label { Text = "a" };

            public Label Second = new Label { Text = "b" };

            public int ThrowA
            {
                get
                {
                    Trace.Next("A");

                    throw new InvalidOperationException("A");
                }
            }

            public int ThrowB
            {
                get
                {
                    Trace.Next("B");

                    throw new InvalidOperationException("B");
                }
            }

            public int OnThis(Engine e) => e.Apply(d: P, f: this.Q);
        }

        [Transpile]
        public class Derived : Engine
        {
            public Derived(Holder h) : base(d: h.P, f: h.Q) { }
        }

        [Transpile]
        public class Tuned(Holder h) : Engine(d: h.P, f: h.Q);

        [Transpile]
        public static class SourceOrderCalls
        {
            public static int Getters(Engine e, Holder h) => e.Apply(d: h.P, f: h.Q);

            public static int GetterAndAuto(Engine e, Holder h) => e.Apply(d: h.Auto, f: h.Bump);

            public static int GetterAndField(Engine e, Holder h) => e.Apply(d: h.Field, f: h.BumpField);

            public static int GetterAndLocal(Engine e, Holder h, int x) => e.Apply(d: x, f: h.P);

            public static int StaticGetter(Engine e, Holder h) => e.Apply(d: Holder.S, f: h.P);

            public static int Construction(Holder h) => new Engine(d: h.P, f: h.Q).Total;

            public static int StaticCall(Holder h) => Engine.Mix(d: h.P, f: h.Q);

            public static int Conditional(Engine? e, Holder h) => e?.Apply(d: h.P, f: h.Q) ?? -1;

            public static int Indexer(Engine e, int[] xs) =>
                e.Apply(d: xs[Trace.Next("i")], f: xs[Trace.Next("j")]);

            public static int GetterReceiver(Holder h) => h.Target.Apply(d: h.P, f: h.Q);

            public static int InterfaceGetter(Engine e, IReading r) => e.Apply(d: r.Value, f: r.Value + 5);

            public static int OverridableAuto(Engine e, Plain p) => e.Apply(d: p.Slot, f: p.Slot);

            public static int OtherAssembly(Engine e, Holder h, string name) =>
                e.Apply(d: name.Length, f: h.Auto);

            public static int Extension(Engine e, Holder h) => e.Scale(d: h.P, f: h.Q);

            public static int ModuleFunction(Holder h) => Mixer.Blend(d: h.P, f: h.Q);

            public static int InterfaceReceiver(IEngine e, Holder h) => e.Apply(d: h.P, f: h.Q);

            public static int Throwing(Engine e, Holder h) => e.Apply(d: h.ThrowA, f: h.ThrowB);

            public static int FormattedOperands(Engine e, Holder h) =>
                e.Join(d: "x" + h.First, f: "y" + h.Second);
        }

        [Transpile]
        public static class PlainCalls
        {
            public static int InsideOneArgument(Engine e, Holder h) => e.Apply(d: h.P - h.Q, f: 7);

            public static int GetterAndConstant(Engine e, Holder h) => e.Apply(d: h.P, f: 7);

            public static int AutoAndField(Engine e, Holder h) => e.Apply(d: h.Auto, f: h.Field);

            public static int TextOperands(Engine e, string s, int n) => e.Join(d: s + n, f: s + "!");

            public static int StaticAutos(Engine e) => e.Apply(d: Holder.Shared, f: Holder.Shared);

            public static int PositionalRecord(Engine e, Point p) => e.Apply(d: p.X, f: p.Y);

            public static int InParameterOrder(Engine e, Holder h) => e.Apply(f: h.P, d: h.Q);
        }
        """;

    /// <summary>
    /// Methods, statics, constructions, extension and module functions, a
    /// null-conditional, a computed and an interface receiver, and both
    /// base-constructor forms bind in source order; two throwing getters
    /// throw the first one written.
    /// </summary>
    [Test]
    public async Task ReorderedComputedReads_BindInSourceOrder()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        var outputs = string.Join(
            "\n",
            files["source-order-calls.ts"],
            files["holder.ts"],
            files["derived.ts"],
            files["tuned.ts"]
        );

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).IsEmpty();
        await TranspileHelper.AssertGolden("overload-calls-computed-read-order.ts", outputs);
    }

    /// <summary>
    /// No order tells these apart, so they stay plain calls: field,
    /// auto-property and positional-record reads alone, text concatenated
    /// with a string or a number, one order-sensitive argument beside a
    /// constant, and a call in parameter order.
    /// </summary>
    [Test]
    public async Task StableOrSingleReads_StayPlainCalls()
    {
        var files = TranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden(
            "overload-calls-stable-read-order.ts",
            files["plain-calls.ts"]
        );
    }
}
