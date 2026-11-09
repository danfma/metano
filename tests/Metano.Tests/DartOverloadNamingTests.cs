using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The Dart names of spec 013 (FR-022, DECIDED-14): a dispatched constructor
/// set becomes named constructors, a dispatched method group one fast path per
/// overload — the core candidates, claimed in Dart's member namespace per
/// class — and every direct call site spells those names. Each expected file
/// was checked with <c>dart analyze</c> and, where it runs, <c>dart run</c>
/// against the C# results.
/// </summary>
public class DartOverloadNamingTests
{
    private const string ReorderMessage =
        "Named arguments out of parameter order with more than one evaluation that C# runs in "
        + "written order are not supported by this target: the call passes them in parameter "
        + "order, so they are evaluated in that order (spec 013 FR-017).";

    [Test]
    public async Task ConstructorOverloads_BecomeNamedConstructors_AndCallSitesSelectThem()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Dimension
            {
                public int Size;
                public int Width;

                public Dimension(int size) => Size = size;

                public Dimension(int width, int height) => Width = width * height;

                public static Dimension Square(int side) => new Dimension(side, side);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "dimension.dart",
            """
            class Dimension {
              Dimension.fromSize(int size) {
                this.size = size;
              }

              Dimension.fromWidthHeight(int width, int height) {
                this.width = width * height;
              }

              late int size;

              late int width;

              static Dimension square(int side) => Dimension.fromWidthHeight(side, side);
            }

            """
        );
    }

    /// <summary>
    /// A <c>: this(...)</c> chain redirects to the named constructor of the
    /// overload it chains to — not the first one — with named arguments placed
    /// by position and a skipped default passed; a record's <c>copyWith</c>
    /// rebuilds through the primary's named constructor.
    /// </summary>
    [Test]
    public async Task ThisChain_RedirectsToTheChainedNamedConstructor_AndCopyWithUsesThePrimary()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record Point(int X, int Y)
            {
                public Point(int v) : this(v, v) { }

                public Point(string s, int y = 7) : this(Y: y, X: s.Length) { }
            }

            [Transpile]
            public class Dim
            {
                public int W;

                public Dim(int size) : this(size, size) { }

                public Dim(int w, int h) => W = w * h;
            }
            """
        );

        // The named arguments read a parameter and a member: two order-sensitive
        // evaluations the redirect passes in parameter order (conservative).
        await Assert.That(diagnostics.Single().Message).IsEqualTo(ReorderMessage);

        await AssertFile(
            files,
            "point.dart",
            """
            import 'package:metano_runtime/metano_runtime.dart' show HashCode;

            class Point {
              Point.fromXY(this.x, this.y);

              Point.fromV(int v) : this.fromXY(v, v);

              Point.fromSY(String s, [int y = 7]) : this.fromXY(s.length, y);

              final int x;

              final int y;

              late final int _y;

              late final int _x;

              @override
              bool operator ==(Object other) => other is Point && other.runtimeType == this.runtimeType && other.x == this.x && other.y == this.y;

              @override
              int get hashCode => HashCode.combine2(this.x, this.y);

              Point copyWith({int? x, int? y}) => Point.fromXY(x ?? this.x, y ?? this.y);
            }

            """
        );

        await AssertFile(
            files,
            "dim.dart",
            """
            class Dim {
              Dim.fromSize(int size) : this.fromWH(size, size);

              Dim.fromWH(int w, int h) {
                this.w = w * h;
              }

              late int w;
            }

            """
        );
    }

    /// <summary>
    /// A chain to an overload whose own chain skips a default: each redirect
    /// names its own target (<c>fromS</c> → <c>fromBig</c> → <c>fromAB</c>),
    /// so <c>Opt("x")</c> returns 79 as in C#.
    /// </summary>
    [Test]
    public async Task ChainOfChains_RedirectsEachOverloadToItsTarget()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Opt
            {
                public int V;

                public Opt(int a, int b = 5) => V = a * 10 + b;

                public Opt(long big) : this((int)big, 9) { }

                public Opt(string s) : this(7L) { }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "opt.dart",
            """
            class Opt {
              Opt.fromAB(int a, [int b = 5]) {
                this.v = a * 10 + b;
              }

              Opt.fromBig(int big) : this.fromAB(big.toSigned(32), 9);

              Opt.fromS(String s) : this.fromBig(7);

              late int v;
            }

            """
        );
    }

    /// <summary>
    /// A chain with a body of its own cannot redirect in Dart: the named
    /// constructor keeps the body, and <c>MS0001</c> is reported at the
    /// constructor.
    /// </summary>
    [Test]
    public async Task ThisChainWithABody_KeepsTheBody_AndReportsMS0001AtTheConstructor()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Box
            {
                public int W;

                public Box(int w) => W = w;

                public Box(string s) : this(s.Length) { W++; }
            }
            """
        );

        var unsupported = diagnostics.Single();

        await Assert.That(unsupported.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(FirstLineOf(unsupported)).StartsWith("public Box(string s)");

        await AssertFile(
            files,
            "box.dart",
            """
            class Box {
              Box.fromW(int w) {
                this.w = w;
              }

              Box.fromS(String s) {
                this.w++;
              }

              late int w;
            }

            """
        );
    }

    /// <summary>
    /// Named arguments out of parameter order are passed in parameter order.
    /// With at most one order-sensitive evaluation the order cannot differ
    /// from C#, so the call stays inline and Dart types each argument from
    /// its parameter (<c>2</c> as a <c>double</c>, <c>[]</c> as a
    /// <c>List&lt;int&gt;</c>): a call with a constant, an assignment beside a
    /// constant (the local receiver is read first in both languages), and a
    /// call whose arguments are all constants.
    /// </summary>
    [Test]
    public async Task ReorderedArguments_WithOneOrderSensitiveEvaluation_StayInline()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Collections.Generic;

            [Transpile]
            public class K
            {
                public double Scale(double d, int n) => d * n;

                public double Scale(string s) => 0;

                public int Count(int n, List<int> items) => n + items.Count;

                public int Count(string s) => 0;
            }

            [Transpile]
            public class Inner
            {
                public int Tag;

                public Inner(int tag) => Tag = tag;

                public int M(int a, int b) => Tag * 100 + a * 10 + b;

                public int M(string s) => 0;
            }

            [Transpile]
            public static class Run
            {
                public static int Next() => 1;

                public static double A(K k) => k.Scale(n: Next(), d: 2);

                public static int B(K k) => k.Count(n: Next(), items: []);

                public static int C()
                {
                    var c = new Inner(1);
                    return c.M(b: (c = new Inner(2)).Tag, a: 3);
                }

                public static int D(Inner i) => i.M(b: 1, a: 2);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "run.dart",
            """
            import 'inner.dart';
            import 'k.dart';

            class Run {
              static int next() => 1;

              static double a(K k) => k.scaleDN(2, Run.next());

              static int b(K k) => k.countNItems(Run.next(), []);

              static int c() {
                var c = Inner(1);
                return c.mAB(3, (c = Inner(2)).tag);
              }

              static int d(Inner i) => i.mAB(2, 1);
            }

            """
        );
    }

    /// <summary>
    /// With two or more order-sensitive evaluations among the reordered
    /// arguments and a receiver that is a computation, Dart would evaluate
    /// them in parameter order: <c>MS0001</c> at the call, which is emitted in
    /// parameter order (#591). Constructions, method calls, a local read
    /// beside a call, a conditional, an <c>await</c> and a closure that writes
    /// a local read by another argument alike.
    /// </summary>
    [Test]
    public async Task ReorderedArguments_WithTwoOrderSensitiveEvaluations_ReportMS0001()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using System.Threading.Tasks;

            [Transpile]
            public class Pair
            {
                public int V;

                public Pair(int a, int b) => V = a * 10 + b;

                public Pair(string s) => V = s.Length;

                public double Scale(double d, int n) => d * n;

                public double Scale(string s) => 0;

                public int Two(int a, int b) => a * 10 + b;

                public int Two(string s) => 0;
            }

            [Transpile]
            public class Holder
            {
                public Pair Inner = new Pair(1, 2);
            }

            [Transpile]
            public static class Use
            {
                public static int Next() => 1;

                public static bool Flag() => true;

                public static Task<int> NextAsync() => Task.FromResult(1);

                public static int Construct() => new Pair(b: Next(), a: Next()).V;

                public static int Call(Pair p) => p.Two(b: Next(), a: Next());

                public static int Receiver(Holder h) => h.Inner.Two(b: Next(), a: 3);

                public static int LocalRead(Pair p)
                {
                    var x = 5;
                    return p.Two(b: Next(), a: x);
                }

                public static double Conditional(Pair p) => p.Scale(n: Next(), d: Flag() ? 2 : 3);

                public static async Task<int> Awaited(Pair p) => p.Two(b: Next(), a: await NextAsync());

                public static int Captured(Pair p)
                {
                    int x = 0;
                    System.Func<int> bump = () => ++x;
                    return p.Two(b: bump(), a: x);
                }
            }
            """
        );

        await Assert
            .That(diagnostics.Select(d => (d.Code, d.Message, FirstLineOf(d))))
            .IsEquivalentTo([
                (
                    DiagnosticCodes.UnsupportedFeature,
                    ReorderMessage,
                    "public static int Construct() => new Pair(b: Next(), a: Next()).V;"
                ),
                (
                    DiagnosticCodes.UnsupportedFeature,
                    ReorderMessage,
                    "public static int Call(Pair p) => p.Two(b: Next(), a: Next());"
                ),
                (
                    DiagnosticCodes.UnsupportedFeature,
                    ReorderMessage,
                    "public static int Receiver(Holder h) => h.Inner.Two(b: Next(), a: 3);"
                ),
                (
                    DiagnosticCodes.UnsupportedFeature,
                    ReorderMessage,
                    "return p.Two(b: Next(), a: x);"
                ),
                (
                    DiagnosticCodes.UnsupportedFeature,
                    ReorderMessage,
                    "public static double Conditional(Pair p) => p.Scale(n: Next(), d: Flag() ? 2 : 3);"
                ),
                (
                    DiagnosticCodes.UnsupportedFeature,
                    ReorderMessage,
                    "public static async Task<int> Awaited(Pair p) => p.Two(b: Next(), a: await NextAsync());"
                ),
                (
                    DiagnosticCodes.UnsupportedFeature,
                    ReorderMessage,
                    "return p.Two(b: bump(), a: x);"
                ),
            ]);

        await AssertFile(
            files,
            "pair.dart",
            """
            class Pair {
              Pair.fromAB(int a, int b) {
                this.v = a * 10 + b;
              }

              Pair.fromS(String s) {
                this.v = s.length;
              }

              late int v;

              double scaleDN(double d, int n) => d * n;

              double scaleS(String s) => 0;

              int twoAB(int a, int b) => a * 10 + b;

              int twoS(String s) => 0;
            }

            """
        );

        await AssertFile(
            files,
            "holder.dart",
            """
            import 'pair.dart';

            class Holder {
              Pair inner = Pair.fromAB(1, 2);
            }

            """
        );

        await AssertFile(
            files,
            "use.dart",
            """
            import 'holder.dart';
            import 'pair.dart';

            class Use {
              static int next() => 1;

              static bool flag() => true;

              static Future<int> nextAsync() => Task.fromResult(1);

              static int construct() => Pair.fromAB(Use.next(), Use.next()).v;

              static int call(Pair p) => p.twoAB(Use.next(), Use.next());

              static int receiver(Holder h) => h.inner.twoAB(3, Use.next());

              static int localRead(Pair p) {
                var x = 5;
                return p.twoAB(x, Use.next());
              }

              static double conditional(Pair p) => p.scaleDN(Use.flag() ? 2 : 3, Use.next());

              static Future<int> awaited(Pair p) async => p.twoAB(await Use.nextAsync(), Use.next());

              static int captured(Pair p) {
                int x = 0;
                int Function() bump = () => ++x;
                return p.twoAB(x, bump());
              }
            }

            """
        );
    }

    /// <summary>
    /// A <c>: this(...)</c> chain with reordered named arguments follows the
    /// same rule as a call: one order-sensitive evaluation redirects inline,
    /// two report <c>MS0001</c> at the initializer, and the redirect passes
    /// them in parameter order.
    /// </summary>
    [Test]
    public async Task ReorderedThisChainArguments_FollowTheOrderRule()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class C
            {
                public int V;

                public static int An() => 1;

                public static int Bn() => 2;

                public C(int a, int b) => V = a * 10 + b;

                public C(string s) : this(b: Bn(), a: An()) { }

                public C(bool f) : this(b: Bn(), a: 7) { }
            }
            """
        );

        var unsupported = diagnostics.Single();

        await Assert.That(unsupported.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
        await Assert.That(unsupported.Message).IsEqualTo(ReorderMessage);

        await Assert
            .That(FirstLineOf(unsupported))
            .IsEqualTo("public C(string s) : this(b: Bn(), a: An()) { }");

        await AssertFile(
            files,
            "c.dart",
            """
            class C {
              C.fromAB(int a, int b) {
                this.v = a * 10 + b;
              }

              C.fromS(String s) : this.fromAB(C.an(), C.bn());

              C.fromF(bool f) : this.fromAB(7, C.bn());

              late int v;

              static int an() => 1;

              static int bn() => 2;
            }

            """
        );
    }

    /// <summary>
    /// A generic construction keeps its explicit type arguments before the
    /// constructor's name (<c>Gen&lt;Object&gt;.fromValue(5)</c>), so Dart does
    /// not infer a narrower type from the arguments.
    /// </summary>
    [Test]
    public async Task GenericConstruction_KeepsItsTypeArguments()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Gen<T>
            {
                public T Value;

                public Gen(T value) => Value = value;

                public Gen(T value, int extra) => Value = value;
            }

            [Transpile]
            public static class Use
            {
                public static Gen<object> Make() => new Gen<object>(5);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "use.dart",
            """
            import 'gen.dart';

            class Use {
              static Gen<Object> make() => Gen<Object>.fromValue(5);
            }

            """
        );
    }

    [Test]
    public async Task MethodOverloads_DeclareFastPaths_AndCallsBindTheSelectedOverload()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Calc
            {
                public int Add(int x) => x;

                public int Add(int x, int y) => x + y;

                [Name("text")]
                public int Add(string s) => s.Length;

                public int Total()
                {
                    System.Func<int, int> f = Add;
                    return Add(1) + Add(2, 3) + Add("abc") + f(4);
                }
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "calc.dart",
            """
            class Calc {
              int addX(int x) => x;

              int addXY(int x, int y) => x + y;

              int addText(String s) => s.length;

              int total() {
                int Function(int) f = this.addX;
                return this.addX(1) + this.addXY(2, 3) + this.addText('abc') + f(4);
              }
            }

            """
        );
    }

    [Test]
    public async Task Overrides_TakeTheRootsName_AndBaseCallsTheRootsFastPath()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Animal
            {
                public virtual string Speak(int n) => "a";

                public virtual string Speak(string s) => "b";
            }

            [Transpile]
            public class Cat : Animal
            {
                public override string Speak(int n) => base.Speak(n) + "!";
            }

            [Transpile]
            public static class Zoo
            {
                public static string Run(Animal a) => a.Speak(1) + a.Speak("x");
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "cat.dart",
            """
            import 'animal.dart';

            class Cat extends Animal {
              String speakN(int n) => super.speakN(n) + '!';
            }

            """
        );

        await AssertFile(
            files,
            "zoo.dart",
            """
            import 'animal.dart';

            class Zoo {
              static String run(Animal a) => a.speakN(1) + a.speakS('x');
            }

            """
        );
    }

    [Test]
    public async Task InterfaceOverloads_DeclareFastPaths_ThatImplementersTake()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public interface IShape
            {
                double Area(double side);

                double Area(double w, double h);
            }

            [Transpile]
            public class Rect : IShape
            {
                public double Area(double side) => side * side;

                public double Area(double w, double h) => w * h;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "i_shape.dart",
            """
            abstract interface class IShape {
              double areaSide(double side);

              double areaWH(double w, double h);
            }

            """
        );

        await AssertFile(
            files,
            "rect.dart",
            """
            import 'i_shape.dart';

            class Rect implements IShape {
              double areaSide(double side) => side * side;

              double areaWH(double w, double h) => w * h;
            }

            """
        );
    }

    /// <summary>
    /// A named constructor conflicts with a static member of its class, not
    /// with an instance one: <c>Money.fromCents</c> meets the static
    /// <c>FromCents</c> and takes <c>$</c>, while <c>Taken.fromSize</c> sits
    /// beside the instance <c>fromSize()</c>. A <c>new</c> method's fast path
    /// that meets an inherited one takes <c>$</c>. Each rename reports
    /// <c>MS0067</c> at the renamed overload.
    /// </summary>
    [Test]
    public async Task TakenNames_TakeTheMark_AndReportMS0067()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public int Cents;

                public Money(decimal amount) => Cents = 1;

                public Money(int cents) => Cents = cents;

                public static Money FromCents(int cents) => new Money(cents);
            }

            [Transpile]
            public class Taken
            {
                public int Size;

                public Taken(int size) => Size = size;

                public Taken(string s) => Size = s.Length;

                public int fromSize() => Size;
            }

            [Transpile]
            public class Animal
            {
                public virtual string Speak(int n) => "a";

                public virtual string Speak(string s) => "b";
            }

            [Transpile]
            public class Dog : Animal
            {
                public new string Speak(int n) => "d";
            }
            """
        );

        await AssertFile(
            files,
            "money.dart",
            """
            class Money {
              Money.fromAmount(Decimal amount) {
                this.cents = 1;
              }

              Money.fromCents$(int cents) {
                this.cents = cents;
              }

              late int cents;

              static Money fromCents(int cents) => Money.fromCents$(cents);
            }

            """
        );

        await AssertFile(
            files,
            "taken.dart",
            """
            class Taken {
              Taken.fromSize(int size) {
                this.size = size;
              }

              Taken.fromS(String s) {
                this.size = s.length;
              }

              late int size;

              int fromSize() => this.size;
            }

            """
        );

        await AssertFile(
            files,
            "dog.dart",
            """
            import 'animal.dart';

            class Dog extends Animal {
              String speakN$(int n) => 'd';
            }

            """
        );

        await Assert
            .That(diagnostics.Select(d => (d.Code, d.Severity, FirstLineOf(d))))
            .IsEquivalentTo([
                (
                    DiagnosticCodes.OverloadNameShadowed,
                    MetanoDiagnosticSeverity.Warning,
                    "public Money(int cents) => Cents = cents;"
                ),
                (
                    DiagnosticCodes.OverloadNameShadowed,
                    MetanoDiagnosticSeverity.Warning,
                    "public new string Speak(int n) => \"d\";"
                ),
            ]);
    }

    /// <summary>
    /// A user member whose name is an inherited fast path would override it:
    /// <c>MS0068</c>, located at the member.
    /// </summary>
    [Test]
    public async Task UserMemberMeetingAnInheritedFastPath_ReportsMS0068()
    {
        var (_, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Animal
            {
                public virtual string Speak(int n) => "a";

                public virtual string Speak(string s) => "b";
            }

            [Transpile]
            public class Parrot : Animal
            {
                public string SpeakN(int n) => "p";
            }
            """
        );

        var hijack = diagnostics.Single();

        await Assert.That(hijack.Code).IsEqualTo(DiagnosticCodes.OverloadFastPathHijacked);
        await Assert.That(hijack.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(FirstLineOf(hijack)).IsEqualTo("public string SpeakN(int n) => \"p\";");
    }

    [Test]
    public async Task ModuleAndExtensionOverloads_DeclareFastPaths_AndCallsUseThem()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [NoContainer]
            [Transpile]
            public static class Text
            {
                public static string Twice(string s) => s + s;

                [Name("text")]
                public static string Twice(int n) => n.ToString();
            }

            [Transpile]
            public static class Ext
            {
                public static int Dbl(this int x) => x * 2;

                public static int Dbl(this string x) => x.Length;
            }

            [Transpile]
            public static class Use
            {
                public static string Run() => Text.Twice("x") + Text.Twice(2) + 3.Dbl() + "ab".Dbl();
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "text.dart",
            """
            String twiceS(String s) => s + s;

            String twiceText(int n) => n.toString();

            """
        );

        await AssertFile(
            files,
            "ext.dart",
            """
            class Ext {
              static int dblInt32(int x) => x * 2;

              static int dblString(String x) => x.length;
            }

            """
        );

        await AssertFile(
            files,
            "use.dart",
            """
            import 'ext.dart';
            import 'text.dart';

            class Use {
              static String run() => twiceS('x') + twiceText(2) + Ext.dblInt32(3) + Ext.dblString('ab');
            }

            """
        );
    }

    /// <summary>
    /// An overloaded operator group keeps one Dart <c>operator</c>
    /// declaration: no fast paths, the source operator at the call site, and
    /// the <c>MS0001</c> warning for the overloads it cannot declare.
    /// </summary>
    [Test]
    public async Task OverloadedOperators_KeepThePrimaryOperator_WithMS0001()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public int Cents;

                public static Money operator +(Money a, Money b) => a;

                public static Money operator +(Money a, int b) => a;

                public static Money Sum(Money a, Money b) => a + b;
            }
            """
        );

        await Assert.That(diagnostics.Single().Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);

        await AssertFile(
            files,
            "money.dart",
            """
            class Money {
              late int cents;

              static Money operator +(Money a, Money b) => a;

              static Money sum(Money a, Money b) => a + b;
            }

            """
        );
    }

    /// <summary>
    /// The keep-today's-emission shapes declare the primary of a dispatched
    /// group under the name its call sites spell: on a branded type the
    /// primary's <c>[Name]</c> pins a suffix, so it is declared <c>plus</c>;
    /// on a <c>[PlainObject]</c> type <c>[Name]</c> renames the member, so it
    /// is declared <c>named</c>.
    /// </summary>
    [Test]
    public async Task KeptPrimaries_AreDeclaredUnderTheirCallSiteNames()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Branded, Transpile]
            public readonly record struct Cents(int Value)
            {
                [Name("text")]
                public Cents Plus(string s, int extra) => new(Value + s.Length + extra);

                public Cents Plus(int n) => new(Value + n);
            }

            [PlainObject, Transpile]
            public record Dto(int A)
            {
                public int Get(int x) => A + x;

                [Name("named")]
                public int Get(string s, int extra) => s.Length;
            }

            [Transpile]
            public static class Use
            {
                public static int Run(Cents c, Dto d) => c.Plus("ab", 1).Value + d.Get("abc", 2);
            }
            """
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .IsEquivalentTo([
                DiagnosticCodes.UnsupportedFeature,
                DiagnosticCodes.UnsupportedFeature,
            ]);

        await AssertFile(
            files,
            "cents.dart",
            """
            class Cents {
              Cents(this.value);

              final int value;

              Cents plus(String s, int extra) => Cents(this.value + s.length + extra);

              @override
              bool operator ==(Object other) => other is Cents && other.runtimeType == this.runtimeType && other.value == this.value;

              @override
              int get hashCode => HashCode.combine(this.value);

              Cents copyWith({int? value}) => Cents(value ?? this.value);
            }

            """
        );

        await AssertFile(
            files,
            "dto.dart",
            """
            class Dto {
              Dto(this.a);

              final int a;

              int named(String s, int extra) => s.length;
            }

            """
        );

        await AssertFile(
            files,
            "use.dart",
            """
            import 'cents.dart';
            import 'dto.dart';

            class Use {
              static int run(Cents c, Dto d) => c.plus('ab', 1).value + d.named('abc', 2);
            }

            """
        );
    }

    /// <summary>
    /// A <c>[Name]</c> targeted at Dart renames a single containerless
    /// function, so a parameter of that name shadows it and takes <c>$</c> —
    /// the parameter-shadow rule reads the target's <c>[Name]</c>.
    /// </summary>
    [Test]
    public async Task ParameterShadowingADartRenamedModuleFunction_TakesTheMark()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [NoContainer]
            [Transpile]
            public static class Logger
            {
                [Name(TargetLanguage.Dart, "log")]
                public static void Write(string message) { }

                public static void Use(string log) => Write(log);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "logger.dart",
            """
            void log(String message) {
            }

            void use(String log$) => log(log$);

            """
        );
    }

    /// <summary>
    /// An overloaded conversion operator group keeps its primary under the
    /// name its <c>[Name]</c> gives it (a conversion operator has no C#
    /// name to pin a suffix on), as before 013g.
    /// </summary>
    [Test]
    public async Task ConversionOperatorGroup_KeepsItsNamedPrimary()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Money
            {
                public int Cents;

                public Money(int cents) => Cents = cents;

                [Name("toInt")]
                public static explicit operator int(Money m) => m.Cents;

                [Name("toLong")]
                public static explicit operator long(Money m) => m.Cents;
            }
            """
        );

        var unsupported = diagnostics.Single();

        await Assert.That(unsupported.Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);

        await Assert
            .That(unsupported.Message)
            .IsEqualTo(
                "Dart target: 'Money.op_Explicit' has 2 overloads but Dart doesn't support "
                    + "overloading it. Emitting the primary signature only; rename the other "
                    + "overloads via [Name(\"...\")] or merge them into a single method with "
                    + "optional parameters."
            );

        await AssertFile(
            files,
            "money.dart",
            """
            class Money {
              Money(int cents) {
                this.cents = cents;
              }

              late int cents;

              static int toInt(Money m) => m.cents;
            }

            """
        );
    }

    /// <summary>
    /// An <c>[Import]</c> class Dart emits as a class gets named constructors
    /// and fast paths, and its constructions call them; an <c>[External]</c>
    /// one, which Dart does not emit, keeps the source construction. One shape
    /// per symbol decides both the declaration and the call site.
    /// </summary>
    [Test]
    public async Task ImportAndExternalTypes_DeclareAndConstructAlike()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            using Metano.Annotations.TypeScript;

            [Transpile, Import("Ext", "ext-lib")]
            public class Ext
            {
                public Ext(int a) { }

                public Ext(string s) { }

                public int Add(int a) => a;

                public int Add(string s) => 1;
            }

            [External]
            public class Host
            {
                public Host(int a) { }

                public Host(string s) { }
            }

            [Transpile]
            public static class Use
            {
                public static Ext Make() => new Ext(2);

                public static int Count(Ext e) => e.Add(1) + e.Add("x");

                public static Host Open() => new Host(3);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "ext.dart",
            """
            class Ext {
              Ext.fromA(int a);

              Ext.fromS(String s);

              int addA(int a) => a;

              int addS(String s) => 1;
            }

            """
        );

        await AssertFile(
            files,
            "use.dart",
            """
            import 'ext.dart';

            class Use {
              static Ext make() => Ext.fromA(2);

              static int count(Ext e) => e.addA(1) + e.addS('x');

              static Host open() => Host(3);
            }

            """
        );
    }

    /// <summary>
    /// A reordered call whose arguments only read computed properties is
    /// still passed in parameter order with no report: the core takes a
    /// getter for free of side effects on this target, so Dart runs
    /// <c>Q</c> before <c>P</c> where C# runs <c>P</c> first. Pins the open
    /// Dart half of #592; the TypeScript target binds such a call.
    /// </summary>
    [Test]
    public async Task ReorderedComputedReads_KeepParameterOrder_AndReportNothing()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public class Holder
            {
                public int Count;

                public int P => ++Count;

                public int Q => ++Count;
            }

            [Transpile]
            public class Engine
            {
                public int Two(int a, int b) => a * 10 + b;

                public int Two(string s) => 0;
            }

            [Transpile]
            public static class Use
            {
                public static int Call(Engine e, Holder h) => e.Two(b: h.P, a: h.Q);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertFile(
            files,
            "use.dart",
            """
            import 'engine.dart';
            import 'holder.dart';

            class Use {
              static int call(Engine e, Holder h) => e.twoAB(h.q, h.p);
            }

            """
        );
    }

    private static string FirstLineOf(MetanoDiagnostic diagnostic) =>
        diagnostic
            .Location!.SourceTree!.GetText()
            .Lines[diagnostic.Location.GetLineSpan().StartLinePosition.Line]
            .ToString()
            .Trim();

    private static async Task AssertFile(
        Dictionary<string, string> files,
        string name,
        string expected
    ) => await Assert.That(files[name]).IsEqualTo(expected);
}
