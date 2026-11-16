using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// <c>Parse</c> / <c>TryParse</c> of the integers of 32 bits or fewer, <c>double</c> and
/// <c>bool</c> lower to the runtime parsers (baseline FR-054, #668), which the rows of
/// <c>tests/BclReference</c> check against real .NET.
/// </summary>
public class IntegerParseMappingTests
{
    [Test]
    public async Task IntegerParse_LowersToTheCheckedParsers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Source(
                """
                public static int A(string s) => int.Parse(s);
                public static int C(string s) => int.Parse(s, NumberStyles.HexNumber);
                public static uint D(string s) => uint.Parse(s);
                public static short E(string s) => short.Parse(s);
                public static ushort F(string s) => ushort.Parse(s);
                public static byte G(string s) => byte.Parse(s);
                public static sbyte H(string s) => sbyte.Parse(s);
                public static bool I(string s, out int n) => int.TryParse(s, NumberStyles.Integer, null, out n);
                public static double J(string s) => double.Parse(s);
                public static bool K(string s) => bool.Parse(s);
                """
            )
        );

        var output = files["text.ts"];

        await Assert.That(output).Contains("return parseInt32(s);");
        await Assert.That(output).Contains("return parseInt32(s, 515);");
        await Assert.That(output).Contains("return parseUInt32(s);");
        await Assert.That(output).Contains("return parseInt16(s);");
        await Assert.That(output).Contains("return parseUInt16(s);");
        await Assert.That(output).Contains("return parseByte(s);");
        await Assert.That(output).Contains("return parseSByte(s);");
        await Assert.That(output).Contains("return parseFloat64(s);");
        await Assert.That(output).Contains("return parseBoolean(s);");
        await Assert.That(output).DoesNotContain("Int32.parse");

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    public async Task IntegerParseWithAProvider_DropsTheProvider()
    {
        var output = TranspileText(
            """
            public static int A(string s) => int.Parse(s, CultureInfo.InvariantCulture);
            """
        );

        await Assert.That(output).Contains("return parseInt32(s);");
    }

    [Test]
    public async Task IntegerParseWithAnUnsupportedStyle_ReportsMs0066()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Source(
                """
                public static int Any(string s) => int.Parse(s, NumberStyles.Any);
                """
            )
        );

        var report = diagnostics.Single(d => d.Code == DiagnosticCodes.UnsupportedInt64Format);

        await Assert.That(report.Message).StartsWith("NumberStyles value 511 is not supported");
    }

    private static string TranspileText(string members) =>
        TranspileHelper.Transpile(Source(members))["text.ts"];

    private static string Source(string members) =>
        $$"""
            using System.Globalization;

            [Transpile]
            public static class Text
            {
            {{members}}
            }
            """;
}
