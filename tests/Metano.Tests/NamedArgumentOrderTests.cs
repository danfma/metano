namespace Metano.Tests;

/// <summary>
/// A call or construction bound to no overload group whose named arguments
/// are out of parameter order keeps C#'s evaluation order (spec 013 FR-017,
/// NFR-004, #597): with two or more order-sensitive arguments the TypeScript
/// target binds them in source order, as it does for a dispatched group.
/// Both goldens compile under <c>tsgo --strict --noImplicitOverride</c>; the
/// same source run under .NET, Bun and Node prints the same trace for every
/// call but <c>OptionalDelegate</c> (#611).
/// </summary>
public class NamedArgumentOrderTests
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
        public class Holder
        {
            public int Auto { get; set; } = 3;

            public int P => Trace.Next("p");

            public int Q => Trace.Next("q");

            public int OnThis(Engine e) => e.Single(d: P, f: this.Q);

            public Engine Target
            {
                get
                {
                    Trace.Next("t");

                    return new Engine();
                }
            }
        }

        [Transpile]
        public delegate int Op(int f, int d);

        [Transpile]
        public class Engine
        {
            public int Single(int f, int d) => f * 10 + d;

            public static int Mix(int f, int d) => f * 10 + d;

            public int Opt(int f, int d = 7, int g = 9) => f * 100 + d * 10 + g;

            public int Rest(int f, int d, params int[] rest) => f * 10 + d + rest.Length * 1000;

            public int Fold(Func<int, int> map, int f, int d) => map(f) * 10 + d;
        }

        [Transpile]
        public class Pair
        {
            public int F;

            public int D;

            public Pair(int f, int d)
            {
                F = f;
                D = d;
            }

            public int Total => F * 10 + D;
        }

        [Transpile]
        public record Rec(int A, int B);

        [Transpile]
        public class Root
        {
            public int Total;

            public Root(int f, int d)
            {
                Total = f * 10 + d;
            }
        }

        [Transpile]
        public class Derived : Root
        {
            public Derived()
                : base(d: Trace.Next("p"), f: Trace.Next("q")) { }
        }

        [Transpile]
        public class PrimaryDerived() : Root(d: Trace.Next("p"), f: Trace.Next("q"));

        [Transpile]
        public class Chained
        {
            public int Total;

            public Chained(int f, int d)
            {
                Total = f * 10 + d;
            }

            public Chained()
                : this(d: Trace.Next("p"), f: Trace.Next("q")) { }
        }

        [Transpile]
        public static class EngineExtensions
        {
            public static int Scale(this Engine e, int f, int d) => f * 10 + d;
        }

        [Transpile, NoContainer]
        public static class Mixer
        {
            public static int Blend(int f, int d) => f * 10 + d;
        }

        [Transpile, PlainObject]
        public record Dto(int F, int D);

        [Transpile]
        public static class Native
        {
            [Emit("($0 * 10 + $1)")]
            public static int Emitted(int f, int d) => f * 10 + d;
        }

        [Transpile, StringEnum]
        public enum Tone
        {
            Low,
            High,
        }

        [Transpile]
        public class Tuner
        {
            public int Tuned(int f, Tone tone, int d) => f * 10 + d + (tone == Tone.High ? 100 : 0);

            public T Pick<T>(T f, T d) => f;

            public int Named(string f, string d) => f.Length * 10 + d.Length;
        }

        [Transpile, NoContainer]
        public static class Boxes
        {
            [ObjectArgs]
            public static int Boxed(int f, int d) => f * 10 + d;
        }

        [Transpile]
        public static class SourceOrderCalls
        {
            public static int Instance(Engine e) => e.Single(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int Getters(Engine e, Holder h) => e.Single(d: h.P, f: h.Q);

            public static int Static() => Engine.Mix(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int New(Holder h) => new Pair(d: h.P, f: h.Q).Total;

            public static int TargetTypedNew()
            {
                Pair pair = new(d: Trace.Next("p"), f: Trace.Next("q"));

                return pair.Total;
            }

            public static int Record()
            {
                var rec = new Rec(B: Trace.Next("p"), A: Trace.Next("q"));

                return rec.A * 10 + rec.B;
            }

            public static int BaseInitializer() => new Derived().Total;

            public static int PrimaryBase() => new PrimaryDerived().Total;

            public static int ThisInitializer() => new Chained().Total;

            public static int Extension(Engine e) => e.Scale(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int ExtensionReceiver(Holder h) =>
                h.Target.Scale(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int Module() => Mixer.Blend(d: Trace.Next("p"), f: Trace.Next("q"));

            public static Op MakeOp()
            {
                Trace.Next("m");

                return (f, d) => f * 10 + d;
            }

            public static int ComputedCallee() => MakeOp()(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int ThisReceiver(Holder h) => h.OnThis(new Engine());

            public static int Delegate()
            {
                Op op = (f, d) => f * 10 + d;

                return op(d: Trace.Next("p"), f: Trace.Next("q"));
            }

            public static int DelegateInvoke()
            {
                Op op = (f, d) => f * 10 + d;

                return op.Invoke(d: Trace.Next("p"), f: Trace.Next("q"));
            }

            public static int FuncDelegate()
            {
                Func<int, int, int> fn = (f, d) => f * 10 + d;

                return fn(arg2: Trace.Next("p"), arg1: Trace.Next("q"));
            }

            public static int OptionalSkipped(Engine e) => e.Opt(g: Trace.Next("p"), f: Trace.Next("q"));

            public static int Params(Engine e) =>
                e.Rest(d: Trace.Next("p"), f: Trace.Next("q"), rest: new[] { 1, 2 });

            public static int Receiver(Holder h) =>
                h.Target.Single(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int NullConditional(Engine? e) =>
                e?.Single(d: Trace.Next("p"), f: Trace.Next("q")) ?? -1;

            public static int LambdaArgument(Engine e) =>
                e.Fold(d: Trace.Next("p"), f: Trace.Next("q"), map: x => x + 1);

            public static int PlainObject()
            {
                var dto = new Dto(D: Trace.Next("p"), F: Trace.Next("q"));

                return dto.F * 10 + dto.D;
            }

            public static int Emit() => Native.Emitted(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int BclMapped() => Math.Max(val2: Trace.Next("p") * 0, val1: Trace.Next("q"));

            public static int Nested(Engine e) =>
                e.Single(d: e.Single(d: Trace.Next("p"), f: Trace.Next("q")), f: Trace.Next("r"));

            public static string Tag(string tag)
            {
                Trace.Next(tag);

                return tag + tag;
            }

            public static int ParamsOmitted(Engine e) => e.Rest(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int StringEnumArgument(Tuner t) =>
                t.Tuned(d: Trace.Next("p"), tone: Tone.High, f: Trace.Next("q"));

            public static int Generic(Tuner t) => t.Pick(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int NullArgument(Tuner t) => t.Named(d: Tag("p"), f: Tag("q") + "x");

            public static int BclInstance() =>
                "abcdefgh".Substring(length: Trace.Next("p"), startIndex: Trace.Next("q")).Length;

            public static int BclException() =>
                new ArgumentException(paramName: Tag("p"), message: Tag("q")).Message.Length;
        }

        [Transpile]
        public static class PlainCalls
        {
            public static int ExpandedParams(Engine e) =>
                e.Rest(f: Trace.Next("p"), Trace.Next("q"), 7, 8);

            public static int NonTrailingNamed(Engine e) => e.Single(f: Trace.Next("p"), Trace.Next("q"));

            public static int InOrder(Engine e) => e.Single(f: Trace.Next("p"), d: Trace.Next("q"));

            public static int OneSensitive(Engine e) => e.Single(d: Trace.Next("p"), f: 4);

            public static int AutoReads(Engine e, Holder h, int x) => e.Single(d: h.Auto, f: x);

            public static int ObjectArgs() => Boxes.Boxed(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int OptionalDelegate(Op? op) =>
                op?.Invoke(d: Trace.Next("p"), f: Trace.Next("q")) ?? -1;

            public static int ParamsOmittedInOrder(Engine e) =>
                e.Rest(f: Trace.Next("p"), d: Trace.Next("q"));

            public static int ConstantFirst(Engine e) => e.Single(d: 4, f: Trace.Next("p"));
        }
        """;

    /// <summary>
    /// Methods, statics, constructions, records, both base-constructor
    /// forms, extension and module functions, delegates, skipped optional
    /// parameters, <c>params</c>, a computed receiver or callee, a
    /// null-conditional call, a <c>[PlainObject]</c> literal, an
    /// <c>[Emit]</c> template, mapped BCL calls and a BCL exception bind
    /// their arguments in source order; a <c>: this(...)</c> chain evaluates
    /// them as written.
    /// </summary>
    [Test]
    public async Task ReorderedNamedArguments_BindInSourceOrder()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        var outputs = string.Join(
            "\n",
            files["source-order-calls.ts"],
            files["holder.ts"],
            files["derived.ts"],
            files["primary-derived.ts"],
            files["chained.ts"]
        );

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).IsEmpty();
        await TranspileHelper.AssertGolden("named-argument-order.ts", outputs);
    }

    /// <summary>
    /// No order tells these apart, so they stay plain calls: named arguments
    /// in parameter order (one of them before positional arguments, or with
    /// the <c>params</c> parameter omitted), one order-sensitive argument
    /// beside a constant, and field and auto-property reads alone. An
    /// <c>[ObjectArgs]</c> literal already lists its properties as written.
    /// A null-conditional delegate call binds each argument to its parameter
    /// and still evaluates them in parameter order (#611).
    /// </summary>
    [Test]
    public async Task OrderedOrSingleEvaluations_StayPlainCalls()
    {
        var files = TranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden("named-argument-plain.ts", files["plain-calls.ts"]);
    }
}
