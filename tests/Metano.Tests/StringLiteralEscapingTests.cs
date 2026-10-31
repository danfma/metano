namespace Metano.Tests;

/// <summary>
/// The TypeScript printer escapes control and line-terminator characters in
/// string literals, so a C# literal holding one prints as a valid, visible
/// JS escape rather than a raw character (a raw newline ends the JS string).
/// </summary>
public class StringLiteralEscapingTests
{
    [Test]
    public async Task ControlCharacters_PrintAsEscapes()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public static class Texts
            {
                public static string Lines() => "a\nb\r\tc\\d\"e";
                public static char Nul() => '\0';
                public static string NulThenDigit() => "\u00001";
                public static string Separators() => "\u2028\u2029\u001b";
            }
            """
        );

        var output = result["texts.ts"];

        await Assert.That(output).Contains("return \"a\\nb\\r\\tc\\\\d\\\"e\";");
        await Assert.That(output).Contains("return \"\\u0000\";");
        await Assert.That(output).Contains("return \"\\u00001\";");
        await Assert.That(output).Contains("return \"\\u2028\\u2029\\u001b\";");
    }
}
