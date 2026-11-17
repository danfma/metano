using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The use sites of user-defined operators and conversions (baseline FR-077,
/// FR-079): each application calls the static helper its type declares
/// (FR-078), compound assignments and increments store through the
/// read-modify-write, lifted forms handle <c>null</c>, a test against a
/// constant <c>null</c> stays a null check (D4), and Dart prints the source
/// operators it printed before.
/// </summary>
public class OperatorUseSiteTests
{
    private const string Source = """
        [Transpile]
        public class Money
        {
            public int C;

            public Money(int c) { C = c; }

            public static Money operator +(Money a, Money b) => new(a.C + b.C);

            public static Money operator -(Money a) => new(-a.C);

            public static Money operator ++(Money a) => new(a.C + 1);

            public static bool operator ==(Money? a, Money? b) => a?.C == b?.C;

            public static bool operator !=(Money? a, Money? b) => !(a == b);

            public static implicit operator Money(int c) => new(c);

            public static explicit operator long(Money m) => m.C;

            public static implicit operator decimal(Money m) => m.C;

            public override bool Equals(object? o) => o is Money m && m.C == C;

            public override int GetHashCode() => C;
        }

        [Transpile]
        public struct Pt
        {
            public int X;

            public Pt(int x) { X = x; }

            public static Pt operator +(Pt a, Pt b) => new(a.X + b.X);

            public static bool operator ==(Pt a, Pt b) => a.X == b.X;

            public static bool operator !=(Pt a, Pt b) => a.X != b.X;

            public static implicit operator Pt(int x) => new(x);

            public override bool Equals(object? o) => o is Pt p && p.X == X;

            public override int GetHashCode() => X;
        }

        [Transpile]
        public static class Uses
        {
            public static Money Arithmetic(Money a, Money b) => (a + b) + -a + 3;

            public static bool Equality(Money a, Money? b) => a == b || b == null || null != a;

            public static long Store(Money m, Money[] arr, int i)
            {
                m += 2;
                m++;
                var before = m++;
                arr[i++] += m;
                return (long)before;
            }

            public static decimal ToDecimal(Money m)
            {
                decimal d = m;
                d += m;
                return d;
            }

            public static Pt? Lifted(Pt? a, Pt b, Pt? c) => a == c ? a + b : null;

            public static Money Sum(int[] raw)
            {
                Money total = 0;

                foreach (Money m in raw)
                    total += m;

                return total;
            }
        }
        """;

    [Test]
    public async Task TypeScript_CallsTheHelpers()
    {
        var result = TranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden("operator-use-sites.ts", result["uses.ts"]);
    }

    [Test]
    public async Task Dart_KeepsTheSourceOperators()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden("operator-use-sites.dart", files["uses.dart"]);
    }

    [Test]
    public async Task AConstantNullTest_StaysANullCheck()
    {
        var output = TranspileHelper.Transpile(Source)["uses.ts"];

        await Assert.That(output).Contains("b == null");
        await Assert.That(output).Contains("null != a");
        await Assert.That(output).DoesNotContain("__equals(b, null)");
    }

    [Test]
    public async Task AMetadataOnlyOperatorGroup_CallsItsDispatcher()
    {
        var files = TranspileHelper.TranspileWithLibraryImage(
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("lib")]

            public class Amount
            {
                public int V;

                public Amount(int v) { V = v; }

                public static Amount operator *(Amount a, int k) => new(a.V * k);

                public static Amount operator *(Amount a, Amount b) => new(a.V * b.V);

                public static explicit operator int(Amount a) => a.V;
            }
            """,
            """
            using Metano.Annotations;
            [assembly: TranspileAssembly]

            public static class Calc
            {
                public static int Triple(Amount a) => (int)(a * 3);
            }
            """
        );

        await Assert
            .That(files["calc.ts"])
            .Contains("return Amount.__toInt32(Amount.__multiply(a, 3));");
    }

    [Test]
    [Arguments(
        """
            [Transpile]
            public class Acc
            {
                public int V;

                public void operator +=(int k) { V += k; }

                public static void Run(Acc a) { a += 2; }
            }
            """,
        "instance operator"
    )]
    [Arguments(
        """
            [Transpile]
            public class Flag
            {
                public bool On;

                public static bool operator true(Flag f) => f.On;

                public static bool operator false(Flag f) => !f.On;

                public static string Run(Flag f) => f ? "on" : "off";
            }
            """,
        "user-defined operator 'op_True'"
    )]
    [Arguments(
        """
            [PlainObject]
            public record Dto(int V)
            {
                public static Dto operator +(Dto a, Dto b) => new(a.V + b.V);
            }

            [Transpile]
            public static class Run
            {
                public static Dto Add(Dto a, Dto b) => a + b;
            }
            """,
        "[PlainObject]"
    )]
    public async Task AnUnsupportedOperator_ReportsMS0001(string source, string message)
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        await Assert
            .That(diagnostics)
            .Contains(d =>
                d.Code == DiagnosticCodes.UnsupportedFeature && d.Message.Contains(message)
            );
    }
}
