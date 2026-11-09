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

        await Assert
            .That(onDoubler.Message)
            .IsEqualTo(
                "[Name(\"other\")] on 'Doubler.Add(int x)' is ignored: an override or "
                    + "interface implementation takes the suffix 'One' of its root "
                    + "'Counter.Add(int x)'; put [Name] on the root (spec 013 FR-005, FR-025)."
            );
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

        await AssertGolden("overload-name-pin-invalid.ts", files["box.ts"]);
        await Assert.That(diagnostics.Count).IsEqualTo(4);

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

        var deadEnd = diagnostics.Single();

        await AssertGolden("overload-name-pin-equal.ts", files["box.ts"]);
        await Assert.That(deadEnd.Code).IsEqualTo(DiagnosticCodes.AmbiguousConstruct);
        await Assert.That(deadEnd.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);

        await Assert
            .That(deadEnd.Message)
            .IsEqualTo(
                "Overloads 'Put(int a)' and 'Put(string b)' have the same suffix at every "
                    + "qualification level, and [Name] pins the suffix of 'Put(int a)' and "
                    + "'Put(string b)'; they are named with the suffixes 'Value$1' and 'Value$2' "
                    + "(spec 013 FR-005, FR-024)."
            );
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

        var deadEnd = diagnostics.Single();

        await AssertGolden("overload-name-pin-dead-end.ts", files["box.ts"]);

        await Assert
            .That(deadEnd.Message)
            .IsEqualTo(
                "Overloads 'F(int a)' and 'F(bool b)' have the same suffix at every "
                    + "qualification level, and [Name] pins the suffix of 'F(bool b)'; they are "
                    + "named with the suffixes 'Int32$1' and 'Int32$2' (spec 013 FR-005, FR-024)."
            );
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
                public int Cents { get; }

                public Fee(string text)
                {
                    Cents = text.Length;
                }

                [Name("cents")]
                public Fee(int value)
                {
                    Cents = value;
                }

                public static Fee FromCents(int value) => new Fee(value);
            }
            """
        );

        var rename = diagnostics.Single();

        await AssertGolden("overload-name-pin-claimed.ts", files["fee.ts"]);
        await Assert.That(rename.Code).IsEqualTo(DiagnosticCodes.OverloadNameShadowed);

        await Assert
            .That(rename.Message)
            .IsEqualTo(
                "Factory 'Fee.fromCents' collides with the user member 'fromCents'; the "
                    + "generated factory and selector are emitted as 'fromCents$' and "
                    + "'byFeeCents$' (spec 013 FR-004)."
            );
    }

    [Test]
    public async Task Naming_IsDeterministic()
    {
        const string source = """
            [Transpile]
            public class Money
            {
                public Money(string text) { }

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
        await AssertGolden("overload-name-pin-deterministic.ts", first);
    }

    /// <summary>
    /// A conversion operator has no C# name, so its <c>[Name]</c> names its
    /// group, overloaded or not, as before 013f; a single unary operator keeps
    /// its <c>[Name]</c> as its whole name too.
    /// </summary>
    [Test]
    public async Task ConversionGroup_IsNamedByItsName_AndPinsNoSuffix()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Money
            {
                public int Cents;

                [Name("toInt")]
                public static explicit operator int(Money m) => m.Cents;

                [Name("toLong")]
                public static explicit operator long(Money m) => m.Cents;

                [Name("negate")]
                public static Money operator -(Money m) => m;
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await AssertGolden("overload-name-pin-conversions.ts", files["money.ts"]);
    }

    /// <summary>
    /// A <c>[PlainObject]</c> type emits no dispatcher, so its methods keep
    /// their <c>[Name]</c> as their whole name where they are called.
    /// </summary>
    [Test]
    public async Task PlainObjectOverloads_KeepTheirNameAtCallSites()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile, PlainObject]
            public record Reading(int Value)
            {
                [Name("getNum")]
                public int Get(int x) => x;

                [Name("getStr")]
                public int Get(string s) => s.Length;
            }

            [Transpile]
            public class Use
            {
                public int Run(Reading r) => r.Get(1);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();

        await AssertGolden(
            "overload-name-pin-plain-object.ts",
            string.Join("\n", files["reading.ts"], files["use.ts"])
        );
    }

    /// <summary>
    /// An override takes its root's suffix, and its own (ignored) <c>[Name]</c>
    /// must not move its type's other overloads: the derived type names its
    /// new overload the same with no pin, a pin naming another type
    /// (<c>string</c>) and a pin naming another parameter (<c>s</c>). The
    /// override sitting next to the root in the visible group dead-ends
    /// whatever the pin (#564).
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("[Name(\"string\")]")]
    [Arguments("[Name(\"s\")]")]
    public async Task OverridePin_DoesNotRenameTheTypesOtherOverloads(string pin)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            $$"""
            [Transpile]
            public class Base
            {
                public virtual int Add(int x) => x;

                public virtual int Add(string s) => s.Length;
            }

            [Transpile]
            public class Derived : Base
            {
                {{pin}}
                public override int Add(int x) => x + 1;

                public int Add(bool b) => 1;
            }
            """
        );

        var namingErrors = diagnostics
            .Where(d => d.Severity == MetanoDiagnosticSeverity.Error)
            .Select(d => d.Message);

        await AssertGolden("overload-name-pin-override-leak.ts", files["derived.ts"]);

        await Assert
            .That(namingErrors)
            .IsEquivalentTo([
                "Overloads 'Add(int x)' and 'Add(int x)' have the same naming spelling at every "
                    + "qualification level; they are named with the suffixes 'Int32$1' and "
                    + "'Int32$2' (spec 013 FR-024).",
            ]);
    }

    [Test]
    public async Task NameOnALoneConstructor_IsReportedAsUnused()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public class Fee
            {
                public int Cents { get; }

                [Name("cents")]
                public Fee(int value)
                {
                    Cents = value;
                }
            }
            """
        );

        var unused = diagnostics.Single();

        await Assert.That(unused.Code).IsEqualTo(DiagnosticCodes.AmbiguousConstruct);
        await Assert.That(unused.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(SourceTextOf(unused)).IsEqualTo("\"cents\"");

        await Assert
            .That(unused.Message)
            .IsEqualTo(
                "[Name(\"cents\")] on the constructor 'Fee(int value)' has no effect: only an "
                    + "overload of a dispatched constructor set gets a factory and a selector to "
                    + "name (spec 013 FR-005)."
            );
    }

    /// <summary>
    /// <c>[JsonFactory]</c> on an overloaded method stays <c>MS0034</c> (the
    /// dispatcher could call another overload), so no descriptor names the
    /// pinned fast path; the companion still pins it (<c>fromParse</c>).
    /// </summary>
    [Test]
    public async Task JsonFactoryOnAPinnedOverload_IsRejected_AndTheCompanionPinsIt()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Text.Json.Serialization;
            using Metano.Annotations.TypeScript;

            [Transpile, Branded]
            public readonly record struct Email(string Value)
            {
                [JsonFactory, Name("parse")]
                public static Email From(string value) => new(value);

                public static Email From(int value) => new(value.ToString());
            }

            [Transpile]
            public sealed record Contact(Email Email);

            [JsonSerializable(typeof(Contact))]
            public partial class JsonContext : JsonSerializerContext
            {
                public JsonContext() : base(new System.Text.Json.JsonSerializerOptions()) { }

                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions =>
                    null;

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(
                    System.Type type
                ) => null;
            }
            """
        );

        var rejected = diagnostics.Single();

        await AssertGolden("overload-name-pin-json-factory.ts", files["email.ts"]);
        await Assert.That(rejected.Code).IsEqualTo(DiagnosticCodes.InvalidJsonFactory);

        await Assert
            .That(rejected.Message)
            .IsEqualTo(
                "[JsonFactory] 'Email.From' is overloaded; the TypeScript dispatcher could call a "
                    + "different overload."
            );
    }

    /// <summary>
    /// Dart honors the pins since 013g (spec 013 FR-005, FR-022): a valid
    /// <c>[Name]</c> pins the overload's fast-path suffix, an invalid one
    /// (<c>put$text</c>) is an <c>MS0003</c> error and keeps the generated
    /// suffix, and an override's <c>[Name]</c> naming another suffix than its
    /// root's is ignored with an <c>MS0003</c> warning.
    /// </summary>
    [Test]
    public async Task Dart_PinsTheSuffix_WithThePinDiagnostics()
    {
        var (files, diagnostics) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;
            [Transpile]
            public class Box
            {
                public int Put(int x) => x;

                [Name("put$text")]
                public int Put(string s) => 0;

                [Name("raw")]
                public int Put(bool b) => 1;
            }

            [Transpile]
            public class Counter
            {
                public virtual int Add(int x) => x;

                public virtual int Add(string s) => s.Length;
            }

            [Transpile]
            public class Doubler : Counter
            {
                [Name("other")]
                public override int Add(int x) => x * 2;
            }
            """
        );

        await Assert
            .That(diagnostics.Select(d => (d.Code, d.Severity)))
            .IsEquivalentTo([
                (DiagnosticCodes.AmbiguousConstruct, MetanoDiagnosticSeverity.Error),
                (DiagnosticCodes.AmbiguousConstruct, MetanoDiagnosticSeverity.Warning),
            ]);

        await Assert
            .That(files["box.dart"])
            .IsEqualTo(
                """
                class Box {
                  int putX(int x) => x;

                  int putS(String s) => 0;

                  int putRaw(bool b) => 1;
                }

                """
            );

        await Assert
            .That(files["doubler.dart"])
            .IsEqualTo(
                """
                import 'counter.dart';

                class Doubler extends Counter {
                  int addX(int x) => x * 2;
                }

                """
            );
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
}
