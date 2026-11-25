namespace Metano.Tests;

/// <summary>
/// A nullable value present by construction reads without the missing-value
/// guard (#646): the predicate sees through <c>checked</c> / <c>unchecked</c>,
/// <c>!</c>, a nested nullable cast, both arms of a conditional and the
/// fallback of <c>??</c>, so TypeScript reports no unreachable <c>??</c>
/// (TS2869). A cast of a value that may be missing keeps the guard.
/// </summary>
public class PresentNullableValueTests
{
    private const string Source = """
        [Transpile]
        public static class Present
        {
            public static int Conditional(bool c) => (c ? (int?)5 : (int?)6).Value;

            public static int MixedConditional(bool c) => (c ? (int?)5 : 6).Value;

            public static int Coalesced(int? n) => (n ?? (int?)5).Value;

            public static (long, long) NestedCast() => ((long, long))((int?)(int?)2, 1);

            public static (long, long) CheckedCast() => ((long, long))(checked((int?)2), 1);

            public static (long, long) Suppressed() => ((long, long))(((int?)2)!, 1);

            public static (long, long) BoxedNull(object o) => ((long, long))((int?)o, 1);

            public static (long, long) NullableLocal(int? n) => ((long, long))((int?)n, 1);
        }
        """;

    [Test]
    public async Task PresentValues_ReadWithoutTheGuard()
    {
        var output = TranspileHelper.Transpile(Source)["present.ts"];

        await Assert.That(output).Contains("return c ? 5 : 6;");
        await Assert.That(output).Contains("return n ?? 5;");
        await Assert.That(output).Contains("return [BigInt(2), 1n] as [bigint, bigint];");
        await Assert.That(output).DoesNotContain("BigInt(2 ??");
    }

    [Test]
    public async Task MaybeMissingValues_KeepTheGuard()
    {
        var output = TranspileHelper.Transpile(Source)["present.ts"];

        await Assert.That(output).Contains("return [BigInt(n ?? throwError(");
        await Assert.That(output).Contains("Nullable object must have a value.");
    }
}
