namespace Metano.Tests;

/// <summary>
/// A <c>decimal</c> cast to an integer of 32 bits or fewer is a target-neutral
/// <c>IrNumericConversion</c> for every target (#604): TypeScript renders the
/// checked runtime narrowing, Dart keeps the cast its numeric printer gives a
/// decimal endpoint, and neither sees the decimal.js call shape the core used
/// to build for Dart.
/// </summary>
public class DecimalNarrowingCastTests
{
    private const string Source = """
        [Transpile]
        public static class Narrowing
        {
            public static int Whole(decimal d) => (int)d;

            public static byte Small(decimal d) => (byte)d;
        }
        """;

    [Test]
    public async Task TypeScript_RendersTheCheckedNarrowing()
    {
        var output = TranspileHelper.Transpile(Source)["narrowing.ts"];

        await Assert.That(output).Contains("return narrowFromDecimal(d, 32, true);");
        await Assert.That(output).Contains("return narrowFromDecimal(d, 8, false);");
    }

    [Test]
    public async Task Dart_ReceivesAConversionNode_NotTheDecimalJsCalls()
    {
        var (files, _) = DartTranspileHelper.Transpile(Source);
        var output = string.Concat(files.Values);

        await Assert.That(output).DoesNotContain("trunc().toNumber()");
        await Assert.That(output).Contains("d as int");
    }
}
