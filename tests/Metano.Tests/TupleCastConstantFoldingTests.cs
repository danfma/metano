using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A constant element of an explicitly cast tuple literal folds in its own
/// type, because the element's conversion is the explicit one the cast
/// applies (#640): an out-of-range element in an <c>unchecked</c> cast no
/// longer aborts the compiler, and a fractional element truncates as the
/// scalar cast does instead of rounding. The runtime side is pinned by the
/// <c>NumericTupleProbes.UncheckedConstantElements</c> conformance row.
/// </summary>
public class TupleCastConstantFoldingTests
{
    private const string Source = """
        [Transpile]
        public static class Casts
        {
            static int I(int x) => x;

            public static (long, long) WrapsULong() => unchecked(((long, long))(18446744073709551615UL, I(1)));

            public static (ulong, long) WrapsNegative() => unchecked(((ulong, long))(-1, I(1)));

            public static (int, long) WrapsLong() => unchecked(((int, long))(3000000000L, I(1)));

            public static (long, long) TruncatesDouble() => ((long, long))(2.7, I(1));
        }
        """;

    [Test]
    public async Task UncheckedOutOfRangeElements_FoldInTheirOwnType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);
        var output = files["casts.ts"];

        await Assert.That(output).Contains("BigInt.asIntN(64, 18446744073709551615n)");
        await Assert.That(output).Contains("BigInt.asUintN(64, BigInt(-1))");
        await Assert.That(output).Contains("Number(BigInt.asIntN(32, 3000000000n))");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task FractionalElement_TruncatesThroughItsConversion()
    {
        var output = TranspileHelper.Transpile(Source)["casts.ts"];

        await Assert.That(output).Contains("int64FromDouble(2.7)");
    }
}
