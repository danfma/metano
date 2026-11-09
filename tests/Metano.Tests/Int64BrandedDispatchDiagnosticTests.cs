using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The <c>MS0063</c> warning for a <c>[Branded]</c> 64-bit type that shares a
/// BigInt-mode dispatcher with another 64-bit type (issue #541): the branded
/// arm is a <c>typeof === "bigint"</c> test, so it accepts what the
/// <c>long</c>, <c>ulong</c> and <c>BigInteger</c> arms accept and the
/// overloads cannot be told apart at runtime. Groups without a branded member
/// are the documented OQ-6 order and report nothing.
/// </summary>
public class Int64BrandedDispatchDiagnosticTests
{
    private const string Preamble = """
        using System.Numerics;

        [Transpile, Branded]
        public readonly record struct UId(long Value);

        [Transpile, Branded]
        public readonly record struct Count(int Value);

        [Transpile, Branded]
        public readonly record struct Slot(long Value);

        """;

    private static IReadOnlyList<MetanoDiagnostic> Ambiguity(
        string source,
        Int64Representation representation = Int64Representation.BigInt
    ) =>
        TranspileHelper
            .TranspileWithDiagnostics(Preamble + source, representation)
            .Diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .ToList();

    [Test]
    public async Task BrandedBesideLongAndUlong_ReportsOneWarningNamingTheGroup()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H2(ulong u) => "ulong";
                public string H2(UId u) => "uid";
                public string H2(long l) => "long";
            }
            """
        );

        var warning = warnings.Single();

        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert
            .That(warning.Message)
            .IsEqualTo(
                "Overloads 'Calls.H2(ulong)', 'Calls.H2(UId)', 'Calls.H2(long)' cannot be told "
                    + "apart at runtime in the BigInt mode: a [Branded] 64-bit type lowers to a "
                    + "bigint, so its dispatcher arm accepts every value the long, ulong and "
                    + "BigInteger arms accept, and a call C# binds to one of them can reach "
                    + "another (ambiguous runtime dispatch). Static call-site dispatch (#25) "
                    + "will resolve it; until then, give the overloads distinct arities or a "
                    + "distinct non-64-bit parameter."
            );
        await Assert.That(warning.Location).IsNotNull();
        await Assert
            .That(warning.Location!.SourceTree!.GetText().ToString(warning.Location.SourceSpan))
            .IsEqualTo("H2");
    }

    [Test]
    public async Task BrandedBesideOneLong_Reports()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(long l) => "long";
                public string H(UId u) => "uid";
            }
            """
        );

        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Message).Contains("'Calls.H(long)', 'Calls.H(UId)'");
    }

    [Test]
    public async Task TwoBrandedOver64Bit_Reports()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(UId u) => "uid";
                public string H(Slot s) => "slot";
            }
            """
        );

        await Assert.That(warnings.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Constructors_Report()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Stamp
            {
                public Stamp(long ticks) { }
                public Stamp(UId id) { }
            }
            """
        );

        var warning = warnings.Single();

        await Assert.That(warning.Message).Contains("'Stamp.Stamp(long)', 'Stamp.Stamp(UId)'");
    }

    [Test]
    public async Task NumberMode_ReportsNothing()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(long l) => "long";
                public string H(UId u) => "uid";
            }
            """,
            Int64Representation.Number
        );

        await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task BrandedOverNon64Bit_BesideLong_ReportsNothing()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(long l) => "long";
                public string H(Count c) => "count";
            }
            """
        );

        await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task Branded64Bit_WithOnlyNon64BitSiblings_ReportsNothing()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(UId u) => "uid";
                public string H(int i) => "int";
                public string H(string s) => "string";
                public string H(UId u, long l) => "pair";
            }
            """
        );

        await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task GroupWithoutBranded_ReportsNothing()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(long l) => "long";
                public string H(ulong u) => "ulong";
                public string H(BigInteger b) => "bigInteger";
            }
            """
        );

        await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task BrandedInAnotherPosition_DoesNotJoinTheGroup()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(string label, UId u) => "uid";
                public string H(string label, long l) => "long";
                public string H(int label, long l) => "int";
            }
            """
        );

        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert
            .That(warnings[0].Message)
            .Contains("'Calls.H(string, UId)', 'Calls.H(string, long)'");
    }
}
