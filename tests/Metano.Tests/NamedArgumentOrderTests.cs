namespace Metano.Tests;

/// <summary>
/// A call or construction bound to no overload group whose named arguments
/// are out of parameter order keeps C#'s evaluation order (spec 013 FR-017,
/// NFR-004, #597): with two or more order-sensitive arguments the TypeScript
/// target binds them in source order, as it does for a dispatched group.
/// The goldens pin the emitted text only; <c>OptionalDelegate</c> pins the
/// parameter-order evaluation left open by #611.
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

            public Op Field = (f, d) => f * 10 + d;

            public Op Fn
            {
                get
                {
                    Trace.Next("g");

                    return (f, d) => f * 10 + d;
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

        [Transpile]
        public static class EngineBlock
        {
            extension(Engine e)
            {
                public int Scale2(int f, int d) => f * 10 + d;
            }
        }

        [Import("NativeBox", from: "../native-box")]
        public class NativeBox
        {
            public int Bias = 500;

            public NativeBox() { }

            public Op Calc => (f, d) => Bias + f * 10 + d;

            public Func<int, int, int> Compute => (f, d) => Bias + f * 10 + d;
        }

        [Transpile]
        public class Collector
        {
            public int RestA(int a, int b, int c, params int[] rest) =>
                a * 100 + b * 10 + c + rest.Length * 1000;

            public int RestL(int a, int b, int c, params System.Collections.Generic.List<int> rest) =>
                a * 100 + b * 10 + c + rest.Count * 1000;

            public int RestE(
                int a,
                int b,
                int c,
                params System.Collections.Generic.IEnumerable<int> rest
            )
            {
                var count = 0;

                foreach (var item in rest)
                    count = count + 1;

                return a * 100 + b * 10 + c + count * 1000;
            }
        }

        [Transpile]
        public class Overloaded
        {
            public int First(out int b, int a, int c)
            {
                b = a * 10 + c;

                return 7;
            }

            public int First(out int b, string a, int c)
            {
                b = a.Length * 10 + c;

                return 8;
            }

            public int Mid(int a, out int b, int c)
            {
                b = a * 10 + c;

                return 7;
            }

            public int Mid(string a, out int b, int c)
            {
                b = a.Length * 10 + c;

                return 8;
            }

            public int Last(int a, int c, out int b)
            {
                b = a * 10 + c;

                return 7;
            }

            public int Last(string a, int c, out int b)
            {
                b = a.Length * 10 + c;

                return 8;
            }

            public static int Shared(out int b, int a, int c)
            {
                b = a * 10 + c;

                return 7;
            }

            public static int Shared(out int b, string a, int c)
            {
                b = a.Length * 10 + c;

                return 8;
            }
        }

        [Transpile]
        public class OutHost
        {
            public int OutFirst(out int b, int a, int c)
            {
                b = a * 10 + c;

                return 7;
            }

            public int OutMid(int a, out int b, int c)
            {
                b = a * 10 + c;

                return 7;
            }

            public bool TryMid(int a, out int b, int c)
            {
                b = a * 10 + c;

                return true;
            }

            public bool TryLast(int a, int c, out int b)
            {
                b = a * 10 + c;

                return true;
            }

            public void VoidOut(out int b, int a, int c)
            {
                b = a * 10 + c;
            }

            public int Two(out int x, int a, out int y, int c)
            {
                x = a;
                y = c;

                return 7;
            }
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

            [Emit("($0.single($1, $2))")]
            public static int OnReceiver(this Engine e, int f, int d) => e.Single(f, d);
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

            public static int DelegateProperty(Holder h) => h.Fn(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int DelegatePropertyInvoke(Holder h) =>
                h.Fn.Invoke(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int DelegateField(Holder h) => h.Field(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int ExtensionBlock(Holder h) =>
                h.Target.Scale2(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int EmitOnReceiver(Holder h) =>
                h.Target.OnReceiver(d: Trace.Next("p"), f: Trace.Next("q"));

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
        public static class OutCalls
        {
            public static int OutFirstInOrder(OutHost o)
            {
                var r = o.OutFirst(a: Trace.Next("p"), c: Trace.Next("q"), b: out var x);

                return r * 100 + x;
            }

            public static int OutFirstReordered(OutHost o)
            {
                var r = o.OutFirst(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x);

                return r * 100 + x;
            }

            public static int OutMid(OutHost o)
            {
                var r = o.OutMid(c: Trace.Next("p"), b: out var x, a: Trace.Next("q"));

                return r * 100 + x;
            }

            public static int OutMidInOrder(OutHost o)
            {
                var r = o.OutMid(a: Trace.Next("p"), c: Trace.Next("q"), b: out var x);

                return r * 100 + x;
            }

            public static int OutLast(OutHost o)
            {
                var ok = o.TryLast(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x);

                return ok ? x : -1;
            }

            public static int IfHead(OutHost o)
            {
                if (o.TryMid(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x))
                    return x;

                return -1;
            }

            public static int IfHeadLast(OutHost o)
            {
                if (o.TryLast(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x))
                    return x;

                return -1;
            }

            public static int Echo(string tag, out int seen)
            {
                seen = Trace.Next(tag);

                return seen;
            }

            public static int IfHeadNested(OutHost o)
            {
                if (o.TryMid(c: Echo("p", out var first), a: Echo("q", out var second), b: out var x))
                    return x * 100 + first * 10 + second;

                return -1;
            }

            public static int VoidOut(OutHost o)
            {
                o.VoidOut(a: Trace.Next("p"), c: Trace.Next("q"), b: out var x);

                return x;
            }

            public static int VoidOutReordered(OutHost o)
            {
                o.VoidOut(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x);

                return x;
            }

            public static int TwoOuts(OutHost o)
            {
                var r = o.Two(c: Trace.Next("p"), a: Trace.Next("q"), y: out var y, x: out var x);

                return r * 100 + x * 10 + y;
            }

            public static int ExistingLocal(OutHost o)
            {
                int x;
                var r = o.OutFirst(a: Trace.Next("p"), c: Trace.Next("q"), b: out x);

                return r * 100 + x;
            }

            public static int OutFirstSingle(OutHost o)
            {
                var r = o.OutFirst(c: 3, b: out var x, a: Trace.Next("p"));

                return r * 100 + x;
            }

            public static int TryGetValue(System.Collections.Generic.Dictionary<string, int> d, string key)
            {
                var ok = d.TryGetValue(value: out var v, key: key);

                return ok ? v : -1;
            }

            public static int TryGetValueCall(System.Collections.Generic.Dictionary<string, int> d)
            {
                if (d.TryGetValue(value: out var v, key: MoreTags.Tag("k")))
                    return v;

                return -1;
            }

            public static int TryParse()
            {
                var ok = int.TryParse(result: out var n, s: MoreTags.Tag("12"));

                return ok ? n : -1;
            }
        }

        [Transpile]
        public static class OverloadedOutCalls
        {
            public static int FirstInOrder(Overloaded o)
            {
                var r = o.First(a: Trace.Next("p"), c: Trace.Next("q"), b: out var x);

                return r * 1000 + x;
            }

            public static int FirstReordered(Overloaded o)
            {
                var r = o.First(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x);

                return r * 1000 + x;
            }

            public static int MidInOrder(Overloaded o)
            {
                var r = o.Mid(a: Trace.Next("p"), c: Trace.Next("q"), b: out var x);

                return r * 1000 + x;
            }

            public static int MidReordered(Overloaded o)
            {
                var r = o.Mid(c: Trace.Next("p"), b: out var x, a: Trace.Next("q"));

                return r * 1000 + x;
            }

            public static int LastReordered(Overloaded o)
            {
                var r = o.Last(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x);

                return r * 1000 + x;
            }

            public static int StaticInOrder()
            {
                var r = Overloaded.Shared(a: Trace.Next("p"), c: Trace.Next("q"), b: out var x);

                return r * 1000 + x;
            }

            public static int StaticReordered()
            {
                var r = Overloaded.Shared(c: Trace.Next("p"), a: Trace.Next("q"), b: out var x);

                return r * 1000 + x;
            }
        }

        [Transpile]
        public static class ParamsCalls
        {
            public static int ArrayOmitted(Collector e) =>
                e.RestA(a: Trace.Next("x"), c: Trace.Next("p"), b: Trace.Next("q"));

            public static int ArrayOmittedLastFirst(Collector e) =>
                e.RestA(c: Trace.Next("p"), a: Trace.Next("x"), b: Trace.Next("q"));

            public static int ArrayGiven(Collector e) =>
                e.RestA(c: Trace.Next("p"), a: Trace.Next("x"), b: Trace.Next("q"), rest: new[] { 1, 2 });

            public static int ListOmitted(Collector e) =>
                e.RestL(a: Trace.Next("x"), c: Trace.Next("p"), b: Trace.Next("q"));

            public static int ListPositionalFirst(Collector e) =>
                e.RestL(Trace.Next("x"), c: Trace.Next("p"), b: Trace.Next("q"));

            public static int ListGiven(Collector e) =>
                e.RestL(a: Trace.Next("x"), c: Trace.Next("p"), b: Trace.Next("q"), rest: [Trace.Next("r")]);

            public static int ListPositional(Collector e) =>
                e.RestL(Trace.Next("x"), Trace.Next("p"), Trace.Next("q"));

            public static int EnumerableOmitted(Collector e) =>
                e.RestE(a: Trace.Next("x"), c: Trace.Next("p"), b: Trace.Next("q"));

            public static int EnumerableOmittedLastFirst(Collector e) =>
                e.RestE(c: Trace.Next("p"), a: Trace.Next("x"), b: Trace.Next("q"));
        }

        [Transpile]
        public static class AmbientCalls
        {
            public static int DelegateProperty(NativeBox box) =>
                box.Calc(d: Trace.Next("p"), f: Trace.Next("q"));

            public static int FuncProperty(NativeBox box) =>
                box.Compute(arg2: Trace.Next("p"), arg1: Trace.Next("q"));

            public static int DelegatePropertyInvoke(NativeBox box) =>
                box.Calc.Invoke(d: Trace.Next("p"), f: Trace.Next("q"));

            public static NativeBox Make()
            {
                Trace.Next("n");

                return new NativeBox();
            }

            public static int ComputedReceiver() => Make().Calc(d: Trace.Next("p"), f: Trace.Next("q"));
        }

        [Transpile]
        public static class MoreTags
        {
            public static string Tag(string tag)
            {
                Trace.Next(tag);

                return tag;
            }
        }

        [Transpile]
        public static class PlainCalls
        {
            public static int ExtensionOneSensitive(Engine e) => e.Scale(d: Trace.Next("p"), f: 4);

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
    /// parameters, <c>params</c>, a computed receiver or callee (a delegate
    /// property or field read whole), an extension-block member, a
    /// null-conditional call, a <c>[PlainObject]</c> literal, an
    /// <c>[Emit]</c> template with and without a receiver, mapped BCL calls
    /// and a BCL exception bind their arguments in source order; a
    /// <c>: this(...)</c> chain evaluates them as written. An omitted
    /// <c>params</c> array or collection takes no position in the order, and
    /// a delegate-typed member of an ambient <c>[Import]</c> type stays a
    /// member call, since its JavaScript side may read <c>this</c>.
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
            files["chained.ts"],
            files["params-calls.ts"],
            files["ambient-calls.ts"]
        );

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).IsEmpty();
        await TranspileHelper.AssertGolden("named-argument-order.ts", outputs);
    }

    /// <summary>
    /// No order tells these apart, so they stay plain calls: named arguments
    /// in parameter order (one of them before positional arguments, or with
    /// the <c>params</c> parameter omitted), one order-sensitive argument
    /// beside a constant (an extension receiver is not one: it always runs
    /// first), and field and auto-property reads alone. An
    /// <c>[ObjectArgs]</c> literal already lists its properties as written.
    /// A null-conditional delegate call binds each argument to its parameter
    /// and still evaluates them in parameter order (#611).
    /// </summary>
    [Test]
    public async Task OrderedOrSingleEvaluations_StayPlainCalls()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).IsEmpty();
        await TranspileHelper.AssertGolden("named-argument-plain.ts", files["plain-calls.ts"]);
    }

    /// <summary>
    /// An <c>out</c> argument evaluates nothing and leaves the call's
    /// argument list, so the order kept is that of the remaining arguments:
    /// with the <c>out</c> parameter first, in the middle or last, two of
    /// them, a <c>void</c> method, an existing local and a statement head.
    /// Arguments left in parameter order once the <c>out</c> ones are gone
    /// (<c>TryGetValue(value: out var v, key: key)</c>) stay plain calls.
    /// The same holds for a call bound to an overload group.
    /// </summary>
    [Test]
    public async Task OutArguments_LeaveTheOrderOfTheOthers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Code)).IsEmpty();
        var outputs = string.Join("\n", files["out-calls.ts"], files["overloaded-out-calls.ts"]);

        await TranspileHelper.AssertGolden("named-argument-out.ts", outputs);
    }
}
