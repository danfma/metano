namespace Metano.Tests;

/// <summary>
/// A bound call whose named arguments are out of parameter order keeps C#'s
/// evaluation order when an argument reads a computed property (spec 013
/// FR-017, NFR-004, #592): the getter may run code, so the TypeScript target
/// binds the arguments in source order as soon as a second argument is
/// order-sensitive. Field and non-overridable auto-property reads alone, a
/// single order-sensitive argument and a call in parameter order stay plain
/// calls. The golden compiles under
/// <c>tsgo --strict --noImplicitOverride</c>; the same source run under
/// .NET, Bun and Node prints the same trace for every call.
/// </summary>
public class OverloadCallComputedReadOrderTests
{
    [Test]
    public async Task ReorderedComputedReads_BindInSourceOrder()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
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
            public class Engine
            {
                public int Total;

                public Engine() { }

                public Engine(int f, int d) { Total = f * 10 + d; }

                public Engine(string mode) { Total = mode.Length; }

                public int Apply(int f, int d) => f * 10 + d;

                public int Apply(string mode) => mode.Length;

                public static int Mix(int f, int d) => f * 10 + d;

                public static int Mix(string mode) => mode.Length;
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

                public int OnThis(Engine e) => e.Apply(d: P, f: this.Q);
            }

            [Transpile]
            public class Derived : Engine
            {
                public Derived(Holder h) : base(d: h.P, f: h.Q) { }
            }

            [Transpile]
            public static class Calls
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

                public static int InsideOneArgument(Engine e, Holder h) => e.Apply(d: h.P - h.Q, f: 7);

                public static int GetterAndConstant(Engine e, Holder h) => e.Apply(d: h.P, f: 7);

                public static int AutoAndField(Engine e, Holder h) => e.Apply(d: h.Auto, f: h.Field);

                public static int StaticAutos(Engine e) => e.Apply(d: Holder.Shared, f: Holder.Shared);

                public static int PositionalRecord(Engine e, Point p) => e.Apply(d: p.X, f: p.Y);

                public static int InParameterOrder(Engine e, Holder h) => e.Apply(f: h.P, d: h.Q);
            }
            """
        );

        var outputs = string.Join("\n", files["calls.ts"], files["holder.ts"], files["derived.ts"]);

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).IsEmpty();
        await TranspileHelper.AssertGolden("overload-calls-computed-read-order.ts", outputs);
    }
}
