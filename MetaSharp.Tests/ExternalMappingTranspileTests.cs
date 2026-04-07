namespace MetaSharp.Tests;

public class ExternalMappingTranspileTests
{
    // ─── [ExportFromBcl] ────────────────────────────────────

    [Test]
    public async Task ExportFromBcl_OverridesHardcodedMapping()
    {
        var result = TranspileHelper.Transpile(
            """
            [assembly: ExportFromBcl(typeof(decimal), FromPackage = "decimal.js", ExportedName = "Decimal")]

            [Transpile]
            public record Price(decimal Amount, string Currency);
            """
        );

        var output = result["price.ts"];
        await Assert.That(output).Contains("amount: Decimal");
        await Assert.That(output).Contains("from \"decimal.js\"");
    }

    [Test]
    public async Task ExportFromBcl_WithoutMapping_UsesBuiltInFallback()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile]
            public record Price(decimal Amount, string Currency);
            """
        );

        var output = result["price.ts"];
        // Without [ExportFromBcl], decimal falls back to number
        await Assert.That(output).Contains("amount: number");
    }

    // ─── [Import] ───────────────────────────────────────────

    [Test]
    public async Task Import_TypeNotGenerated()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, Import("Decimal", from: "decimal.js")]
                public class ExternalDecimal { }

                [Transpile]
                public record Price(int Cents);
            }
            """
        );

        await Assert.That(result).DoesNotContainKey("external-decimal.ts");
        await Assert.That(result).ContainsKey("price.ts");
    }

    [Test]
    public async Task Import_ReferencedTypeGeneratesCorrectImport()
    {
        var result = TranspileHelper.Transpile(
            """
            namespace App
            {
                [Transpile, Import("Moment", from: "moment")]
                public class Moment { }

                [Transpile]
                public record Event(string Name, Moment When);
            }
            """
        );

        var output = result["event.ts"];
        await Assert.That(output).Contains("from \"moment\"");
        await Assert.That(output).Contains("when: Moment");
    }

    // ─── [Emit] ─────────────────────────────────────────────

    [Test]
    public async Task Emit_MethodNotGeneratedInOutput()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Helpers
            {
                [Emit("$0.toFixed($1)")]
                public static extern string ToFixed(decimal value, int digits);

                public static int Double(int x) => x * 2;
            }
            """
        );

        var output = result["helpers.ts"];
        await Assert.That(output).DoesNotContain("toFixed");
        await Assert.That(output).Contains("double");
    }

    [Test]
    public async Task Emit_InlinedAtCallSite()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Utils
            {
                [Emit("typeof $0")]
                public static extern string TypeOf(object value);

                public static string CheckType(object x)
                {
                    return TypeOf(x);
                }
            }
            """
        );

        var output = result["utils.ts"];
        await Assert.That(output).Contains("typeof x");
    }

    [Test]
    public async Task Emit_MultipleArguments()
    {
        var result = TranspileHelper.Transpile(
            """
            [Transpile, ExportedAsModule]
            public static class Fmt
            {
                [Emit("$0.slice($1, $2)")]
                public static extern string Slice(string str, int start, int end);

                public static string Mid(string s)
                {
                    return Slice(s, 1, 3);
                }
            }
            """
        );

        var output = result["fmt.ts"];
        await Assert.That(output).Contains("s.slice(1, 3)");
    }
}
