namespace Metano.Tests;

/// <summary>
/// <c>long</c> and <c>ulong</c> overload dispatch must stay distinguishable in
/// Number mode (#450, PR #473): both primitives lower to <c>number</c>, so the
/// fast-path helpers get distinct suffixes (<c>Long</c> / <c>ULong</c>) and the
/// dispatcher checks <c>ulong</c> with <c>isUInt64</c>, not <c>isInt64</c>
/// (which would admit negatives).
/// </summary>
public class Int64OverloadNamingTests
{
    [Test]
    public async Task MethodOverloads_LongAndULong_GetDistinctFastPaths()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Gates
            {
                public string Pick(long x) => "long";

                public string Pick(ulong x) => "ulong";
            }
            """
        );

        var output = result["gates.ts"];

        await Assert.That(output).Contains("import { isInt64, isUInt64 } from \"metano-runtime\";");
        await Assert.That(output).Contains("private pickLong(x: number)");
        await Assert.That(output).Contains("private pickULong(x: number)");
        await Assert
            .That(output)
            .Contains("isInt64(args[0])) {\n      return this.pickLong(args[0] as number);");
        await Assert
            .That(output)
            .Contains("isUInt64(args[0])) {\n      return this.pickULong(args[0] as number);");
    }

    [Test]
    public async Task OperatorOverloads_LongAndULong_GetDistinctHelpers()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public class Acct
            {
                public static Acct operator +(Acct a, long b) => a;

                public static Acct operator +(Acct a, ulong b) => a;
            }
            """
        );

        var output = result["acct.ts"];

        await Assert.That(output).Contains("import { isInt64, isUInt64 } from \"metano-runtime\";");
        await Assert.That(output).Contains("__addAcctLong(");
        await Assert.That(output).Contains("__addAcctULong(");
        await Assert.That(output).Contains("isUInt64(");
    }
}
