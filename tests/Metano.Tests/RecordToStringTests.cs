using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A record carries .NET's synthesized <c>ToString</c> text (baseline FR-093)
/// through the <c>$printMembers</c> protocol (FR-094): synthesized only where
/// Roslyn declares the implicit <c>ToString</c>, with C#'s printable-member rule
/// and the interpolation default text of each member.
/// </summary>
public class RecordToStringTests
{
    [Test]
    public async Task RootRecord_PrintsItsMembersThroughPrintMembers()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Item(string Name, int Qty);
            """
        );

        await TranspileHelper.AssertGolden("record-to-string.ts", files["item.ts"]);
    }

    [Test]
    public async Task DerivedRecord_ChainsToTheBaseTextFirst()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Base(string Name);

            [Transpile]
            public record Derived(string Name, int Level) : Base(Name);

            [Transpile]
            public record DerivedEmpty(string Name) : Base(Name);
            """
        );

        await TranspileHelper.AssertGolden("record-to-string-derived.ts", files["derived.ts"]);

        await Assert
            .That(files["derived-empty.ts"])
            .Contains("return super.$printMembers?.() ?? \"\";");

        await Assert
            .That(files["derived-empty.ts"])
            .Contains("return members ? `DerivedEmpty { ${members} }` : \"DerivedEmpty { }\";");
    }

    [Test]
    public async Task SealedRootAndRecordStruct_InlineTheirMembers()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public sealed record Sealed(int A);

            [Transpile]
            public sealed record SealedEmpty;

            [Transpile]
            public record struct Point(int X, int Y);
            """
        );

        await Assert.That(files["sealed.ts"]).Contains("return `Sealed { A = ${this.a} }`;");
        await Assert.That(files["sealed.ts"]).DoesNotContain("$printMembers");
        await Assert.That(files["sealed-empty.ts"]).Contains("return \"SealedEmpty { }\";");

        await Assert
            .That(files["point.ts"])
            .Contains("return `Point { X = ${this.x}, Y = ${this.y} }`;");
    }

    [Test]
    public async Task MembersPrintTheInterpolationDefaultText()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public enum Color { Red, Green }

            [Transpile]
            public record Values(float F, double D, bool B, Color E, string? S, int? N, char C);
            """
        );

        var values = files["values.ts"];

        await Assert.That(values).Contains("F = ${formatSingle(this.f)}");
        await Assert.That(values).Contains("D = ${formatDouble(this.d)}");
        await Assert.That(values).Contains("B = ${formatBoolean(this.b)}");
        await Assert.That(values).Contains("E = ${enumText(this.e, Color$runtimeType)}");
        await Assert.That(values).Contains("S = ${this.s ?? \"\"}");
        await Assert.That(values).Contains("N = ${this.n ?? \"\"}");
        await Assert.That(values).Contains("C = ${this.c}");
    }

    [Test]
    public async Task PrintableMembers_FollowCSharpRule()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile]
            public record Body(string Name, [property: Ignore] int Skipped)
            {
                public int Extra { get; init; } = 7;
                public int Computed => 42;
                public string Field = "f";
                private int _hidden = 1;
                public static int Static = 3;
                public const int Constant = 4;
                protected int Prot => 1;
                public int PrivGet { private get; init; } = 5;
                public int Hidden() => _hidden;
            }

            [Transpile]
            public abstract record VirtualBase { public virtual int V => 1; }

            [Transpile]
            public record VirtualDerived : VirtualBase { public override int V => 2; public int W => 3; }
            """
        );

        await Assert
            .That(files["body.ts"])
            .Contains(
                "return `Name = ${this.name ?? \"\"}, Extra = ${this.extra}, Computed = ${this.computed}, Field = ${this.field ?? \"\"}, PrivGet = ${this.privGet}`;"
            );

        await Assert.That(files["virtual-derived.ts"]).Contains("W = ${this.w}");
        await Assert.That(files["virtual-derived.ts"]).DoesNotContain("V = ");
    }

    [Test]
    public async Task DisplayName_IsTheSimpleCSharpName()
    {
        var files = TranspileHelper.Transpile(
            """
            using Metano.Annotations;

            [Transpile, Name("Renamed")]
            public record Generic<T>(T Value);
            """
        );

        await Assert
            .That(files.Values.Single(f => f.Contains("class Renamed")))
            .Contains("`Generic { ${this.$printMembers()} }`");
    }

    [Test]
    public async Task UserAndSealedToString_AreLeftUntouched()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Custom(int A) { public override string ToString() => "custom"; }

            [Transpile]
            public record SealedBase(int A) { public sealed override string ToString() => "sealed"; }

            [Transpile]
            public record AfterSealed(int A, int B) : SealedBase(A);

            [Transpile]
            public abstract record AbstractBase(int A) { public abstract override string ToString(); }

            [Transpile]
            public record AfterAbstract(int A, int B) : AbstractBase(A);
            """
        );

        await Assert.That(files["custom.ts"]).Contains("return \"custom\";");
        await Assert.That(files["custom.ts"]).DoesNotContain("`Custom {");

        await Assert
            .That(files["after-sealed.ts"])
            .Contains("protected override $printMembers(): string");

        await Assert.That(files["after-sealed.ts"]).DoesNotContain("toString(): string");
        await Assert.That(files["after-abstract.ts"]).Contains("override toString(): string");
    }

    [Test]
    public async Task UserPrintMembersAndToStringOverload_ReportMs0001()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            [Transpile]
            public record Printer(int A)
            {
                protected virtual bool PrintMembers(System.Text.StringBuilder builder) => true;
            }

            [Transpile]
            public record AfterPrinter(int A, int B) : Printer(A);

            [Transpile]
            public record Formatted(int A) { public string ToString(string format) => format; }
            """
        );

        var warnings = diagnostics
            .Where(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature
                && d.Message.Contains("synthesized ToString()")
            )
            .Select(d => d.Message)
            .ToList();

        await Assert.That(warnings.Count).IsEqualTo(3);
        await Assert.That(files["printer.ts"]).DoesNotContain("$printMembers");
        await Assert.That(files["after-printer.ts"]).DoesNotContain("$printMembers");
        await Assert.That(files["formatted.ts"]).Contains("toString(format: string): string");
        await Assert.That(files["formatted.ts"]).Contains("protected $printMembers(): string");
    }

    [Test]
    public async Task PlainObjectAtATextSite_ReportsMs0001()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using Metano.Annotations;

            [Transpile, PlainObject]
            public record Dto(int A);

            [Transpile]
            public record Holder(Dto Inner);

            [Transpile]
            public static class Use
            {
                public static string Text(Dto dto) => $"x {dto}" + dto + dto.ToString();
            }
            """
        );

        var plainObject = diagnostics.Where(d => d.Message.Contains("[PlainObject]")).ToList();

        await Assert.That(plainObject.Count).IsEqualTo(3);

        await Assert
            .That(plainObject.All(d => d.Severity == MetanoDiagnosticSeverity.Warning))
            .IsTrue();
    }

    [Test]
    public async Task CrossPackageBase_IsReadTolerantly()
    {
        var files = TranspileHelper.TranspileWithLibrary(
            """
            [assembly: Metano.Annotations.TranspileAssembly]
            [assembly: Metano.Annotations.EmitPackage("lib")]

            namespace Lib;

            public record LibBase(string Name);
            """,
            """
            [assembly: Metano.Annotations.TranspileAssembly]

            public record Local(string Name, int Level) : Lib.LibBase(Name);
            """
        );

        await Assert
            .That(files["local.ts"])
            .Contains("const base = super.$printMembers?.() ?? \"\";");

        await Assert.That(files["local.ts"]).Contains("override toString(): string");
    }

    [Test]
    public async Task Dart_OutputIsUnchanged()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public record Item(string Name, int Qty);
            """
        );

        await Assert.That(string.Concat(files.Values)).DoesNotContain("printMembers");
    }
}
