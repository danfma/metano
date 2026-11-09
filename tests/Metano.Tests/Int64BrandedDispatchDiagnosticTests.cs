using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The <c>MS0063</c> warning for a branded 64-bit type that shares a
/// dispatcher with another <c>bigint</c>-valued type (issue
/// #541): the branded arm is a <c>typeof === "bigint"</c> test, so it accepts
/// what the <c>long</c>, <c>ulong</c> and <c>BigInteger</c> arms accept and
/// the overloads cannot be told apart through the dispatcher. Groups without a
/// branded member are the documented OQ-6 order and report nothing.
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

        [Transpile, InlineWrapper]
        public readonly record struct Wrapped(long Value);

        [Transpile, Branded]
        public readonly record struct NativeId(nint Value);

        [Transpile, Branded]
        public readonly record struct Big(BigInteger Value);

        """;

    private static string MessageFor(string members) =>
        $"Overloads {members} share one runtime test: a branded 64-bit type ([Branded] or "
        + "[InlineWrapper] over long, ulong, nint, nuint or BigInteger) lowers to a bigint, so "
        + "its dispatcher arm accepts every value the long, ulong and BigInteger arms accept. "
        + "A call that reaches the dispatcher (hand-written TypeScript, or a consumer reading "
        + "this type from a DLL or package) can run another overload than the one C# binds; "
        + "calls transpiled from C# in this compilation use the overload's own fast path and "
        + "are not affected. To make the dispatcher unambiguous, give the overloads distinct "
        + "arities or a distinct non-64-bit parameter.";

    private static IReadOnlyList<MetanoDiagnostic> Ambiguity(string source) =>
        TranspileHelper
            .TranspileWithDiagnostics(Preamble + source)
            .Diagnostics.Where(d => d.Code == DiagnosticCodes.AmbiguousRuntimeDispatch)
            .ToList();

    private static async Task AssertAnchoredAt(MetanoDiagnostic warning, string declarationText)
    {
        var location = warning.Location!;

        var line = location.SourceTree!.GetText().Lines[
            location.GetLineSpan().StartLinePosition.Line
        ];

        await Assert.That(line.ToString()).Contains(declarationText);
    }

    private static async Task AssertReportsOnly(string source, string members)
    {
        var warning = Ambiguity(source).Single();

        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);
        await Assert.That(warning.Message).IsEqualTo(MessageFor(members));
        await Assert.That(warning.Location).IsNotNull();
    }

    [Test]
    public async Task BrandedBesideLongAndUlong_WarnsOnceAtTheBrandedMember()
    {
        var warning = Ambiguity(
                """
                [Transpile]
                public class Calls
                {
                    public string H2(ulong u) => "ulong";
                    public string H2(UId u) => "uid";
                    public string H2(long l) => "long";
                }
                """
            )
            .Single();

        await Assert.That(warning.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(warning.Message)
            .IsEqualTo(MessageFor("'Calls.H2(ulong)', 'Calls.H2(UId)', 'Calls.H2(long)'"));

        await AssertAnchoredAt(warning, "public string H2(UId u)");
    }

    [Test]
    public Task BrandedBesideOneLong_WarnsAboutTheUnreachableArm() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string H(long l) => "long";
                public string H(UId u) => "uid";
            }
            """,
            "'Calls.H(long)', 'Calls.H(UId)'"
        );

    [Test]
    public Task TwoBrandedOver64Bit_WarnsNamingBoth() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string H(UId u) => "uid";
                public string H(Slot s) => "slot";
            }
            """,
            "'Calls.H(UId)', 'Calls.H(Slot)'"
        );

    [Test]
    public Task InlineWrapperBesideLong_Warns() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string H(long l) => "long";
                public string H(Wrapped w) => "wrapped";
            }
            """,
            "'Calls.H(long)', 'Calls.H(Wrapped)'"
        );

    [Test]
    public Task BrandedOverNint_BesideLong_Warns() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string H(long l) => "long";
                public string H(NativeId n) => "native";
            }
            """,
            "'Calls.H(long)', 'Calls.H(NativeId)'"
        );

    [Test]
    public Task BrandedOverBigInteger_BesideBigInteger_Warns() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string H(BigInteger b) => "big";
                public string H(Big b) => "branded";
            }
            """,
            "'Calls.H(BigInteger)', 'Calls.H(Big)'"
        );

    [Test]
    public Task ConstructorGroup_WarnsNamingTheConstructors() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Stamp
            {
                public Stamp(long ticks) { }
                public Stamp(UId id) { }
            }
            """,
            "'Stamp.Stamp(long)', 'Stamp.Stamp(UId)'"
        );

    [Test]
    public Task BrandedInAnotherPosition_GroupsOnlyTheMatchingPair() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string H(string label, UId u) => "uid";
                public string H(string label, long l) => "long";
                public string H(int label, long l) => "int";
            }
            """,
            "'Calls.H(string, UId)', 'Calls.H(string, long)'"
        );

    [Test]
    public Task ParamsArrays_WarnByTheirElementType() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string H(params long[] l) => "long";
                public string H(params UId[] u) => "uid";
            }
            """,
            "'Calls.H(params long[])', 'Calls.H(params UId[])'"
        );

    [Test]
    public Task ParamsBesideFixedPosition_Warns() =>
        AssertReportsOnly(
            """
            [Transpile]
            public class Calls
            {
                public string K(string label, UId u) => "uid";
                public string K(string label, params long[] l) => "long";
            }
            """,
            "'Calls.K(string, UId)', 'Calls.K(string, params long[])'"
        );

    [Test]
    public async Task DerivedTypeAddingABrandedOverload_WarnsAtTheAddedMember()
    {
        var warning = Ambiguity(
                """
                [Transpile]
                public class Base
                {
                    public virtual string H(long l) => "long";
                    public virtual string H(string s) => "string";
                }

                [Transpile]
                public class Derived : Base
                {
                    public override string H(long l) => "derived long";
                    public string H(UId u) => "uid";
                }
                """
            )
            .Single();

        await Assert
            .That(warning.Message)
            .IsEqualTo(MessageFor("'Derived.H(long)', 'Derived.H(UId)'"));

        await AssertAnchoredAt(warning, "public string H(UId u)");
    }

    [Test]
    public async Task InterfaceImplementingClassAddingABrandedOverload_Warns()
    {
        var warning = Ambiguity(
                """
                [Transpile]
                public interface IPick
                {
                    string H(long l);
                    string H(string s);
                }

                [Transpile]
                public class Pick : IPick
                {
                    public string H(long l) => "long";
                    public string H(string s) => "string";
                    public string H(UId u) => "uid";
                }
                """
            )
            .Single();

        await Assert.That(warning.Message).IsEqualTo(MessageFor("'Pick.H(long)', 'Pick.H(UId)'"));

        await AssertAnchoredAt(warning, "public string H(UId u)");
    }

    [Test]
    public async Task DerivedTypeOverridingOnly_ReportsNothing()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Base
            {
                public virtual string H(long l) => "long";
                public virtual string H(string s) => "string";
            }

            [Transpile]
            public class Derived : Base
            {
                public override string H(long l) => "derived long";
            }
            """
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
    public async Task BrandedPositionsInDifferentOrder_AreDifferentGroups()
    {
        var warnings = Ambiguity(
            """
            [Transpile]
            public class Calls
            {
                public string H(UId u, string s) => "uid first";
                public string H(string s, UId u) => "uid last";
            }
            """
        );

        await Assert.That(warnings).IsEmpty();
    }
}
