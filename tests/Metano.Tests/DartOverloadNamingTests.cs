using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The Dart names of spec 013 (FR-022, DECIDED-14): a dispatched constructor
/// set becomes named constructors, a dispatched method group one fast path per
/// overload — the core candidates, claimed in Dart's single member namespace
/// per class — and every direct call site spells those names.
/// </summary>
public class DartOverloadNamingTests
{
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
        await Assert
            .That(files["dimension.dart"])
            .IsEqualTo(
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

    [Test]
    public async Task ThisChain_RedirectsToTheTargetsNamedConstructor_AndCopyWithUsesThePrimary()
    {
        var (files, _) = DartTranspileHelper.Transpile(
            """
            [Transpile]
            public record Point(int X, int Y)
            {
                public Point(int v) : this(v, v) { }

                public Point(string s, int y = 7) : this(Y: y, X: s.Length) { }
            }
            """
        );

        var dart = files["point.dart"];

        await Assert.That(dart).Contains("Point.fromXY(this.x, this.y);");
        await Assert.That(dart).Contains("Point.fromV(int v) : this.fromXY(v, v);");
        await Assert
            .That(dart)
            .Contains("Point.fromSY(String s, [int y = 7]) : this.fromXY(s.length, y);");
        await Assert.That(dart).Contains("=> Point.fromXY(x ?? this.x, y ?? this.y);");
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
        await Assert
            .That(files["calc.dart"])
            .IsEqualTo(
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
        var (files, _) = DartTranspileHelper.Transpile(
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

        await Assert
            .That(files["cat.dart"])
            .Contains("String speakN(int n) => super.speakN(n) + '!';");
        await Assert
            .That(files["zoo.dart"])
            .Contains("static String run(Animal a) => a.speakN(1) + a.speakS('x');");
    }

    [Test]
    public async Task InterfaceOverloads_DeclareFastPaths_ThatImplementersTake()
    {
        var (files, _) = DartTranspileHelper.Transpile(
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

        await Assert.That(files["i_shape.dart"]).Contains("double areaSide(double side);");
        await Assert.That(files["i_shape.dart"]).Contains("double areaWH(double w, double h);");
        await Assert.That(files["rect.dart"]).Contains("double areaSide(double side) =>");
        await Assert.That(files["rect.dart"]).Contains("double areaWH(double w, double h) =>");
    }

    /// <summary>
    /// Dart claims named constructors and fast paths in one table per class
    /// (DECIDED-18): a named constructor that meets a user member, and a
    /// <c>new</c> method's fast path that meets an inherited one, take
    /// <c>$</c> and report <c>MS0067</c>.
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

        await Assert.That(files["money.dart"]).Contains("Money.fromCents$(int cents)");
        await Assert
            .That(files["money.dart"])
            .Contains("static Money fromCents(int cents) => Money.fromCents$(cents);");
        await Assert.That(files["dog.dart"]).Contains("String speakN$(int n) => 'd';");
        await Assert
            .That(diagnostics.Select(d => (d.Code, d.Severity)))
            .IsEquivalentTo([
                (DiagnosticCodes.OverloadNameShadowed, MetanoDiagnosticSeverity.Warning),
                (DiagnosticCodes.OverloadNameShadowed, MetanoDiagnosticSeverity.Warning),
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
        await Assert.That(hijack.Location).IsNotNull();
    }

    [Test]
    public async Task ModuleAndExtensionOverloads_DeclareFastPaths_AndCallsUseThem()
    {
        var (files, _) = DartTranspileHelper.Transpile(
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

        await Assert.That(files["text.dart"]).Contains("String twiceS(String s) => s + s;");
        await Assert.That(files["text.dart"]).Contains("String twiceText(int n) =>");
        await Assert.That(files["ext.dart"]).Contains("static int dblInt32(int x) => x * 2;");
        await Assert.That(files["ext.dart"]).Contains("static int dblString(String x) =>");
        await Assert
            .That(files["use.dart"])
            .Contains("twiceS('x') + twiceText(2) + Ext.dblInt32(3) + Ext.dblString('ab')");
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

        await Assert.That(files["money.dart"]).DoesNotContain("__add");
        await Assert.That(files["money.dart"]).Contains("=> a + b;");
        await Assert.That(diagnostics.Single().Code).IsEqualTo(DiagnosticCodes.UnsupportedFeature);
    }

    /// <summary>
    /// A <c>[Name]</c> targeted at Dart renames a single containerless
    /// function, so a parameter of that name shadows it and takes <c>$</c> —
    /// the parameter-shadow rule reads the target's <c>[Name]</c>.
    /// </summary>
    [Test]
    public async Task ParameterShadowingADartRenamedModuleFunction_TakesTheMark()
    {
        var (files, _) = DartTranspileHelper.Transpile(
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

        await Assert.That(files["logger.dart"]).Contains("void use(String log$) => log(log$);");
    }
}
