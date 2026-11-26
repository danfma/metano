namespace Metano.Tests;

/// <summary>
/// An explicit tuple cast unwraps every nullable element whose target is
/// non-nullable, with the .NET <c>InvalidOperationException</c>, even when
/// the element needs no numeric conversion (#630); a getter source is read
/// once. The runtime side is pinned by the <c>NumericTupleProbes</c>
/// conformance rows.
/// </summary>
public class TupleCastNullableElementTests
{
    private const string Source = """
        [Transpile]
        public class Holder
        {
            public ((int, int), int) Nested => ((1, 2), 3);
        }

        [Transpile]
        public static class Casts
        {
            public static (int, int) Unconverted((int?, int) s) => ((int, int))s;

            public static ((int, int), long) NestedTuple(((int, int)?, int) s) => (((int, int), long))s;

            public static (int, int) Literal(int? n) => ((int, int))(n, 6);

            public static ((long, long), long) Getter(Holder h) => (((long, long), long))h.Nested;
        }
        """;

    [Test]
    public async Task NullableElementWithoutConversion_UnwrapsWithTheThrow()
    {
        var output = TranspileHelper.Transpile(Source)["casts.ts"];

        await Assert.That(output).Contains("return [n ?? throwError(");
    }

    [Test]
    public async Task NullableTupleSourceElements_UnwrapWithTheThrow()
    {
        var output = TranspileHelper.Transpile(Source)["casts.ts"];
        var guardedElements = output.Split("return [s[0] ?? throwError(").Length - 1;

        await Assert.That(guardedElements).IsEqualTo(2);
    }

    [Test]
    public async Task GetterSource_IsReadOnce()
    {
        var output = TranspileHelper.Transpile(Source)["casts.ts"];

        await Assert.That(output).Contains("const receiver$temp$0 = h.nested;");
        await Assert.That(output).DoesNotContain("h.nested[");
    }
}
