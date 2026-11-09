using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>[Name]</c> on an overload (spec 013 slice 013f, FR-005, OQ-1 (a)): it
/// pins the overload's generated suffix — <c>[Name("cents")]</c> gives
/// <c>fromCents</c>, <c>byMoneyCents</c>, <c>addCents</c> — and leaves the
/// dispatcher's C# name alone. The pin takes part in the group's distinctness
/// checks but never climbs (contracts/naming.md § Stage 1, step 7); an
/// override or interface implementation takes its root's names (FR-025); an
/// invalid identifier reports <c>MS0003</c>. Every golden type-checks under
/// <c>tsgo --strict --noImplicitOverride</c>.
/// </summary>
public class OverloadNamePinTests
{
    [Test]
    public async Task Pin_RenamesTheFactorySelectorFastPathAndOperatorSuffixes()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile]
            public class Money
            {
                public int Cents { get; }

                public Money(string text)
                {
                    Cents = text.Length;
                }

                [Name("cents")]
                public Money(int value)
                {
                    Cents = value;
                }

                public Money Add(Money other) => new Money(Cents + other.Cents);

                [Name("cents")]
                public Money Add(int value) => Add(new Money(value));

                public string Tag(int x) => "int";

                [Name("x")]
                public string Tag(string s) => s;

                [Name(TargetLanguage.TypeScript, "flag")]
                public string Tag(bool b) => "bool";

                [Name("plus")]
                public Money Single(Money other) => Add(other);

                [Name("scaled")]
                public static Money operator *(Money a, int b) => new Money(a.Cents * b);

                public static Money operator *(Money a, string b) => new Money(b);

                public Money Grow() => Add(1) * 2;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await AssertGolden("overload-name-pin-class.ts", files["money.ts"]);
    }

    [Test]
    public async Task Pin_OnARoot_NamesItsOverridesAndImplementations_TheirOwnPinIsIgnored()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public interface IShape
            {
                [Name("side")]
                int Area(int s);

                int Area(int w, int h);
            }

            [Transpile]
            public class Square : IShape
            {
                [Name("ignored")]
                public int Area(int side) => side * side;

                public int Area(int w, int h) => w * h;
            }

            [Transpile]
            public class Counter
            {
                [Name("one")]
                public virtual int Add(int x) => x;

                public virtual int Add(string s) => s.Length;
            }

            [Transpile]
            public class Doubler : Counter
            {
                [Name("other")]
                public override int Add(int x) => x * 2;
            }

            [Transpile]
            public class Tripler : Counter
            {
                [Name("one")]
                public override int Add(int x) => x * 3;
            }

            [Transpile]
            public class Use
            {
                public int Run(IShape shape, Counter counter) => shape.Area(2) + counter.Add(3);
            }
            """
        );

        var outputs = string.Join(
            "\n",
            files["i-shape.ts"],
            files["square.ts"],
            files["counter.ts"],
            files["doubler.ts"],
            files["tripler.ts"],
            files["use.ts"]
        );

        // Types transform in parallel, so the reports arrive in any order.
        var ignored = diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousConstruct).ToList();
        var onSquare = ignored.Single(d => d.Message.Contains("'Square.Area(int side)'"));
        var onDoubler = ignored.Single(d => d.Message.Contains("'Doubler.Add(int x)'"));

        await AssertGolden("overload-name-pin-roots.ts", outputs);
        await Assert.That(diagnostics.Count).IsEqualTo(2);
        await Assert.That(ignored.Count).IsEqualTo(2);
        await Assert
            .That(ignored.All(d => d.Severity == MetanoDiagnosticSeverity.Warning))
            .IsTrue();
        await Assert
            .That(onSquare.Message)
            .IsEqualTo(
                "[Name(\"ignored\")] on 'Square.Area(int side)' is ignored: an override or "
                    + "interface implementation takes the suffix 'Side' of its root "
                    + "'IShape.Area(int s)'; put [Name] on the root (spec 013 FR-005, FR-025)."
            );
        await Assert.That(SourceTextOf(onSquare)).IsEqualTo("\"ignored\"");
        await Assert.That(onDoubler.Message).Contains("[Name(\"other\")]");
        await Assert.That(onDoubler.Message).Contains("the suffix 'One'");
    }

    [Test]
    public async Task Pin_OnModuleAndExtensionOverloads_RenamesTheExportedFastPaths()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [assembly: TranspileAssembly]

            [ExportedAsModule]
            public static class Greets
            {
                public static int Twice(int x) => x * 2;

                [Name("text")]
                public static int Twice(string s) => s.Length * 2;

                public static int Thrice(this int x) => x * 3;

                [Name("ofText")]
                public static int Thrice(this string s) => s.Length * 3;
            }

            [Transpile]
            public class Caller
            {
                public static int Direct() => Greets.Twice("abcd") + Greets.Twice(1);

                public static int AsExtension() => "abcd".Thrice() + 7.Thrice();
            }
            """
        );

        var outputs = string.Join("\n", files["greets.ts"], files["caller.ts"], files["index.ts"]);

        await Assert.That(diagnostics).IsEmpty();
        await AssertGolden("overload-name-pin-module.ts", outputs);
    }

    [Test]
    public async Task Pin_ReachesProjectReferenceCallSites()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithLibraryAndDiagnostics(
            PinLibrary,
            PinConsumer
        );

        await Assert.That(diagnostics).IsEmpty();
        await AssertGolden("overload-name-pin-cross-package.ts", files["app/register.ts"]);
    }

    /// <summary>
    /// DECIDED-24: a producer read from metadata only keeps the dispatcher
    /// calls, and the dispatcher keeps the C# name whatever its overloads pin.
    /// </summary>
    [Test]
    public async Task MetadataOnlyReference_CallsTheDispatcherByItsCSharpName()
    {
        var files = TranspileHelper.TranspileWithLibraryImage(PinLibrary, PinConsumer);

        await AssertGolden("overload-name-pin-cross-package-metadata.ts", files["app/register.ts"]);
    }

    [Test]
    public async Task InvalidPin_ReportsMS0003AtTheArgument_AndKeepsTheGeneratedSuffix()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Box
            {
                public int Put(int x) => x;

                [Name("2nd")]
                public int Put(string s) => 0;

                [Name("a-b")]
                public int Put(bool b) => 1;

                [Name("x$")]
                public int Put(double d) => 2;

                [Name("")]
                public int Put(char c) => 3;
            }
            """
        );

        var invalid = diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousConstruct).ToList();

        await Assert
            .That(invalid.Select(SourceTextOf))
            .IsEquivalentTo(["\"2nd\"", "\"a-b\"", "\"x$\"", "\"\""]);
        await Assert.That(invalid.All(d => d.Severity == MetanoDiagnosticSeverity.Error)).IsTrue();
        await Assert
            .That(invalid.Single(d => SourceTextOf(d) == "\"2nd\"").Message)
            .IsEqualTo(
                "[Name(\"2nd\")] on the overload 'Put(string s)' is not a valid suffix: it must "
                    + "start with an ASCII letter or '_' and contain only ASCII letters, digits "
                    + "and '_'; the overload keeps its generated suffix (spec 013 FR-005)."
            );
        await Assert.That(files["box.ts"]).Contains("putS(s: string)");
        await Assert.That(files["box.ts"]).Contains("putB(b: boolean)");
        await Assert.That(files["box.ts"]).Contains("putD(d: number)");
        await Assert.That(files["box.ts"]).Contains("putC(c: string)");
    }

    [Test]
    public async Task EqualPins_AreADeadEnd_WithPositionalMarkers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Box
            {
                [Name("value")]
                public int Put(int a) => a;

                [Name("value")]
                public int Put(string b) => 0;
            }
            """
        );

        var deadEnd = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousConstruct);

        await Assert.That(deadEnd.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert
            .That(deadEnd.Message)
            .IsEqualTo(
                "Overloads 'Put(int a)' and 'Put(string b)' have the same suffix at every "
                    + "qualification level, and [Name] pins the suffix of 'Put(int a)' and "
                    + "'Put(string b)'; they are named with the suffixes 'Value$1' and 'Value$2' "
                    + "(spec 013 FR-005, FR-024)."
            );
        await Assert.That(files["box.ts"]).Contains("putValue$1(a: number)");
        await Assert.That(files["box.ts"]).Contains("putValue$2(b: string)");
    }

    /// <summary>
    /// A pin collides with the generated suffixes like any suffix: here the
    /// parameter names collide, the computed overloads switch to their type
    /// spellings and the pin meets <c>Int32</c>, which cannot climb.
    /// </summary>
    [Test]
    public async Task PinMeetingASpellingThatCannotClimb_IsADeadEnd()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Box
            {
                public int F(int a) => a;

                public int F(string a) => 0;

                [Name("int32")]
                public int F(bool b) => 1;
            }
            """
        );

        var deadEnd = diagnostics.Single(d => d.Code == DiagnosticCodes.AmbiguousConstruct);

        await Assert.That(deadEnd.Message).Contains("'F(int a)' and 'F(bool b)'");
        await Assert.That(deadEnd.Message).Contains("[Name] pins the suffix of 'F(bool b)'");
        await Assert.That(files["box.ts"]).Contains("fInt32$1(a: number)");
        await Assert.That(files["box.ts"]).Contains("fString(a: string)");
        await Assert.That(files["box.ts"]).Contains("fInt32$2(b: boolean)");
    }

    /// <summary>
    /// The claim pass treats a pinned name like any candidate (FR-004): a user
    /// member that already holds it moves the generated pair to <c>$</c>.
    /// </summary>
    [Test]
    public async Task PinTakenByAUserMember_IsRenamedByTheClaimPass()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Fee
            {
                public decimal Amount { get; }

                public Fee(decimal amount)
                {
                    Amount = amount;
                }

                [Name("cents")]
                public Fee(int value)
                {
                    Amount = value / 100m;
                }

                public static Fee FromCents(int value) => new Fee(value * 1m);
            }
            """
        );

        var rename = diagnostics.Single(d => d.Code == DiagnosticCodes.OverloadNameShadowed);

        await Assert
            .That(rename.Message)
            .IsEqualTo(
                "Factory 'Fee.fromCents' collides with the user member 'fromCents'; the "
                    + "generated factory and selector are emitted as 'fromCents$' and "
                    + "'byFeeCents$' (spec 013 FR-004)."
            );
        await Assert.That(files["fee.ts"]).Contains("static fromCents$(value: number): Fee");
        await Assert.That(files["fee.ts"]).Contains("static fromCents(value: number): Fee");
    }

    [Test]
    public async Task Naming_IsDeterministic()
    {
        const string source = """
            [Transpile]
            public class Money
            {
                public Money(decimal amount) { }

                [Name("cents")]
                public Money(int value) { }

                public int Add(int x) => x;

                [Name("x")]
                public int Add(string s) => 0;
            }
            """;

        var first = TranspileHelper.Transpile(source)["money.ts"];
        var second = TranspileHelper.Transpile(source)["money.ts"];

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(first).Contains("addInt32(x: number)");
        await Assert.That(first).Contains("addX(s: string)");
    }

    private static string SourceTextOf(MetanoDiagnostic diagnostic) =>
        diagnostic.Location!.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static async Task AssertGolden(string name, string actual)
    {
        if (TranspileHelper.UpdateGoldensRequested)
        {
            TranspileHelper.WriteExpected(name, actual);

            return;
        }

        await Assert.That(actual).IsEqualTo(TranspileHelper.ReadExpected(name));
    }

    private const string PinLibrary = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("lib")]

        namespace Lib;

        public class Money
        {
            public decimal Amount { get; }

            public Money(decimal amount) { Amount = amount; }

            [Name("cents")]
            public Money(int value) { Amount = value / 100m; }

            public Money Add(Money other) => new Money(Amount + other.Amount);

            [Name("cents")]
            public Money Add(int value) => Add(new Money(value));
        }

        [ExportedAsModule]
        public static class Pricing
        {
            public static Money Discount(Money m, int percent) => m;

            [Name("flat")]
            public static Money Discount(Money m, Money off) => m;
        }
        """;

    private const string PinConsumer = """
        using Metano.Annotations;
        [assembly: TranspileAssembly]

        namespace App;

        public class Register
        {
            public Lib.Money Total() =>
                new Lib.Money(150).Add(25).Add(new Lib.Money(1m));

            public Lib.Money Sale(Lib.Money m) =>
                Lib.Pricing.Discount(Lib.Pricing.Discount(m, 10), new Lib.Money(5));
        }
        """;
}
