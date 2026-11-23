namespace Metano.Tests;

/// <summary>
/// A numeric literal in a <c>decimal?</c> or <c>BigInteger?</c> slot is a
/// decimal (or big integer) value, as in a non-nullable one: no conversion
/// node follows a literal, so <c>decimal? d = 5</c> printed the number
/// <c>5</c>, on which every <c>Decimal</c> method fails (#606).
/// </summary>
public class NullableDecimalLiteralTests
{
    [Test]
    public async Task LiteralInNullableDecimalSlot_IsADecimal()
    {
        var output = TranspileHelper.Transpile(
            """
            using System.Numerics;

            [Transpile]
            public static class Amounts
            {
                public static decimal? Five()
                {
                    decimal? d = 5;

                    return d;
                }

                public static BigInteger? Big()
                {
                    BigInteger? b = 7;

                    return b;
                }
            }
            """
        )["amounts.ts"];

        await Assert.That(output).Contains("const d = new Decimal(\"5\");");
        await Assert.That(output).Contains("const b = 7n;");
    }
}
