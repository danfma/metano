namespace Metano.Tests;

/// <summary>
/// <c>new BigInteger(x)</c> builds a <c>bigint</c>; the type has no
/// constructor at runtime, and the unmapped call printed <c>new Object(x)</c>
/// (#677).
/// </summary>
public class BigIntegerConstructorTests
{
    [Test]
    [Arguments("int v", "BigInt(v)")]
    [Arguments("uint v", "BigInt(v)")]
    [Arguments("long v", "BigInt(v)")]
    [Arguments("ulong v", "BigInt(v)")]
    [Arguments("double v", "bigIntegerFromDouble(v)")]
    [Arguments("float v", "bigIntegerFromDouble(v)")]
    [Arguments("decimal v", "bigIntegerFromDecimal(v)")]
    public async Task Constructor_FromANumber_LowersToABigInt(string parameter, string expected)
    {
        var output = Transpile($"public static BigInteger Make({parameter}) => new BigInteger(v);");

        await Assert.That(output).Contains($"return {expected};");
        await Assert.That(output).DoesNotContain("new Object");
    }

    [Test]
    public async Task DefaultConstructor_IsZero()
    {
        var output = Transpile("public static BigInteger Make() => new BigInteger();");

        await Assert.That(output).Contains("return 0n;");
    }

    [Test]
    public async Task ConversionHelpers_AreImportedFromTheRuntime()
    {
        var output = Transpile("public static BigInteger Make(double v) => new BigInteger(v);");

        await Assert
            .That(output)
            .Contains("import { bigIntegerFromDouble } from \"metano-runtime\";");
    }

    private static string Transpile(string members) =>
        TranspileHelper.Transpile(
            $$"""
            using System.Numerics;

            [Transpile]
            public static class Big
            {
                {{members}}
            }
            """
        )["big.ts"];
}
