namespace Metano.Tests;

/// <summary>
/// The implicit and explicit conversions from the integers of 32 bits or
/// fewer to <c>decimal</c> (baseline FR-080, #476), and the stores into a
/// <c>decimal</c> (compound assignments, <c>++</c> / <c>--</c>), which call
/// the runtime <c>Decimal</c>'s methods. Dart keeps its output.
/// </summary>
public class IntegerToDecimalConversionTests
{
    private const string Source = """
        [Transpile]
        public readonly record struct Cash
        {
            public decimal A { get; }

            public Cash(int cents)
            {
                A = cents;
            }
        }

        [Transpile]
        public static class Uses
        {
            public static decimal FromInt(int i) => i;

            public static decimal Half(decimal d) => d / 2m;

            public static decimal Argument(ushort s) => Half(s);

            public static decimal Sum(decimal d, sbyte b) => d + b;

            public static bool Less(uint u, decimal d) => u < d;

            public static decimal? Lifted(int? n) => n;

            public static decimal Coalesce(int? n) => n ?? 1.5m;

            public static decimal Cast(short s) => (decimal)s;

            public static decimal Compound(decimal d, int i)
            {
                d += i;
                d -= 1;
                d *= i;
                d /= 4m;
                d %= 3m;
                return d;
            }

            public static decimal Steps(decimal[] arr, int k)
            {
                var d = arr[k]++;
                --arr[k];
                return d;
            }

            public static decimal FromLong(long l) => l;

            public static bool Equal(decimal? d, int i, long l, char c) =>
                d == i || d != l || d == c;
        }
        """;

    [Test]
    public async Task TypeScript_ConvertsEachIntegerAndCallsTheDecimalMethodsOnEveryStore()
    {
        var result = TranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden("integer-to-decimal.ts", result["uses.ts"]);
        await Assert.That(result["cash.ts"]).Contains("this.a = new Decimal(cents);");
    }

    [Test]
    public async Task Dart_KeepsItsOutput()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);

        await TranspileHelper.AssertGolden("integer-to-decimal.dart", files["uses.dart"]);
    }
}
