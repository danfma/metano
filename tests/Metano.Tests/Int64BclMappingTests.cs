using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The BCL mappings of spec 012 slice 012g (FR-035–FR-044, FR-072) in the
/// staging <see cref="Int64Representation.BigInt"/> mode: the 64-bit
/// overloads lower to the <c>metano-runtime</c> helpers, while the shipped
/// <see cref="Int64Representation.Number"/> mode never sees a <c>bigint</c>
/// template. Every golden type-checks under <c>tsgo --strict</c>.
/// </summary>
public class Int64BclMappingTests
{
    private const string MathSource = """
        [Transpile]
        public class Statics
        {
            public long Abs(long l) => Math.Abs(l) + long.Abs(l);
            public long Max(long l, long m) => Math.Max(l, m) + long.Max(l, m);
            public long Min(long l, long m) => Math.Min(l, m) + long.Min(l, m);
            public long Widened(int i, long l) => Math.Max(i, l);
            public ulong Unsigned(ulong u, ulong v) => Math.Max(u, v) + ulong.Min(u, v);
            public int Sign(long l, ulong u) => Math.Sign(l) + long.Sign(l) + ulong.Sign(u);
            public long Clamp(long l) => Math.Clamp(l, -10, 10) + long.Clamp(l, 0, 1);
            public ulong UClamp(ulong u) => Math.Clamp(u, 1, 2);
            public (long, long) DivRem(long l, long m) => Math.DivRem(l, m);
            public (ulong, ulong) UDivRem(ulong u, ulong v) => ulong.DivRem(u, v);

            public long DivRemOut(long l, long m)
            {
                var quotient = Math.DivRem(l, m, out var rest);
                return quotient + rest;
            }

            public long BigMul(long l, long m)
            {
                var high = Math.BigMul(l, m, out long low);
                return high ^ low;
            }

            public ulong UBigMul(ulong u, ulong v)
            {
                var high = Math.BigMul(u, v, out ulong low);
                return high ^ low;
            }

            public double Pow(long l) => Math.Pow(l, 2);
            public double Doubles(double a, double b) => Math.Max(a, b) + Math.Abs(a);
        }
        """;

    [Test]
    public async Task BigIntMode_MathStatics_CallTheInt64Helpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            MathSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["statics.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-math.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The shipped <c>Number</c> mode skips the <c>bigint</c> templates
    /// (<c>WhenInt64IsBigInt</c>): <c>Math.Max</c> keeps its JS rename and the
    /// statics that had no mapping stay unmapped.
    /// </summary>
    [Test]
    public async Task NumberMode_MathStatics_KeepTheShippedLowering()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            MathSource,
            Int64Representation.Number
        );

        var statics = files["statics.ts"];

        await Assert.That(statics).Contains("return Math.max(l, m) + Int64.max(l, m);");
        await Assert.That(statics).Contains("return Math.abs(l) + Int64.abs(l);");
        await Assert.That(statics).DoesNotContain("Int64(");
        await Assert.That(statics).DoesNotContain("metano-runtime");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsNotEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
