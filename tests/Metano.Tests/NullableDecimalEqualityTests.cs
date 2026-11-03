namespace Metano.Tests;

/// <summary>
/// A Decimal is an object, so <c>==</c> over a <c>decimal?</c> compared
/// references (#323). It lowers to the null-safe value equality instead; a
/// comparison with the <c>null</c> literal stays a plain null check.
/// </summary>
public class NullableDecimalEqualityTests
{
    [Test]
    public async Task Equality_OverNullableDecimals_ComparesByValueNullSafely()
    {
        var output = Transpile(
            """
            public static bool Same(decimal? a, decimal? b) => a == b;
            public static bool Differ(decimal? a, decimal? b) => a != b;
            public static bool WithValue(decimal? a, decimal b) => a == b;
            public static bool WithLiteral(decimal? a) => a == 1.5m;
            """
        );

        await Assert.That(output).Contains("return valueEquals(a, b);");
        await Assert.That(output).Contains("return !valueEquals(a, b);");
        await Assert.That(output).Contains("import { valueEquals } from \"metano-runtime\";");
    }

    [Test]
    public async Task Equality_WithAConvertedNumericOperand_ComparesDecimals()
    {
        var output = Transpile(
            """
            public static bool IsZero(decimal? d) => d == 0;
            public static bool Differs(decimal? d, int i) => d != i;
            """
        );

        await Assert.That(output).Contains("return valueEquals(d, new Decimal(0));");
        await Assert.That(output).Contains("return !valueEquals(d, new Decimal(i));");
    }

    [Test]
    public async Task Equality_WithANullableNumericOperand_KeepsNullOutOfTheDecimalConstructor()
    {
        var output = Transpile("public static bool Same(decimal? d, int? i) => d == i;");

        await Assert.That(output).Contains("i == null ? null : new Decimal(i)");
    }

    [Test]
    public async Task Equality_WithTheNullLiteral_StaysANullCheck()
    {
        var output = Transpile(
            """
            public static bool IsNone(decimal? a) => a == null;
            public static bool IsSome(decimal? a) => a != null;
            """
        );

        await Assert.That(output).Contains("return a == null;");
        await Assert.That(output).Contains("return a != null;");
        await Assert.That(output).DoesNotContain("valueEquals");
    }

    [Test]
    public async Task Equality_OverNonNullableDecimals_KeepsTheDecimalMethod()
    {
        var output = Transpile("public static bool Same(decimal a, decimal b) => a == b;");

        await Assert.That(output).DoesNotContain("valueEquals");
    }

    private static string Transpile(string members) =>
        TranspileHelper.Transpile(
            $$"""
            #nullable enable
            using Metano.Annotations;

            [Transpile]
            public static class Money
            {
                {{members}}
            }
            """
        )["money.ts"];
}
