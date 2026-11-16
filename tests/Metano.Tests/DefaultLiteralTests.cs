using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// A target-typed <c>default</c> literal and <c>default(T)</c> resolve to the
/// default of the type the compiler converts them to, wherever they appear
/// (#627, #372, #612): never the <c>undefined</c> the opaque marker prints.
/// </summary>
public class DefaultLiteralTests
{
    private const string Types = """
        using System.Collections.Generic;
        using System.Linq;

        [Transpile]
        public enum Level { Low = 1, High = 2 }

        [Transpile]
        public enum Mode { Off, On }

        [Transpile]
        public struct Point { public int X; }

        [Transpile, StringEnum]
        public enum Tone { Soft = 1, Loud = 2 }
        """;

    [Test]
    [Arguments("bool b = default; return b;", "const b = false;")]
    [Arguments("int x = default; return x;", "const x = 0;")]
    [Arguments("long l = default; return l;", "const l = 0n;")]
    [Arguments("decimal m = default; return m;", "const m = new Decimal(\"0\");")]
    [Arguments("char c = default; return c;", "const c = \"\\u0000\";")]
    [Arguments("Mode m = default; return m;", "const m = Mode.Off;")]
    [Arguments("Level v = default; return v;", "const v = 0 as Level;")]
    [Arguments("string? s = default; return s;", "const s = null as string | null;")]
    [Arguments("int? n = default; return n;", "const n = null as number | null;")]
    [Arguments("var x = default(int); return x;", "const x = 0;")]
    [Arguments("var b = default(bool); return b;", "const b = false;")]
    public async Task LocalInitializer_TakesTheDefaultOfItsType(string body, string lowered)
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + $$"""

                [Transpile]
                public class Holder
                {
                    public object? Run() { {{body}} }
                }
                """
        );

        await Assert.That(files["holder.ts"]).Contains(lowered);
        await Assert.That(Warnings(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task NestedPositions_TakeTheDefaultOfTheirConvertedType()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + """

                [Transpile]
                public class Holder
                {
                    public int Conditional(List<int> xs, bool cond) => xs.FirstOrDefault(cond ? 9 : default);

                    public int Coalesced(List<int> xs) => xs.FirstOrDefault(defaultValue: (int?)null ?? default);

                    public int FromLocal(List<int> xs)
                    {
                        int d = default;

                        return xs.FirstOrDefault(d);
                    }

                    public int Coalesce(int? n) => n ?? default;

                    public List<int> Elements() => [default, 1];

                    public Mode Switched(bool cond) => cond ? Mode.On : default;
                }
                """
        );

        var holder = files["holder.ts"];

        await Assert.That(holder).Contains("firstOrDefault(undefined, cond ? 9 : 0)");
        await Assert.That(holder).Contains("firstOrDefault(undefined, null ?? 0)");
        await Assert.That(holder).Contains("const d = 0;");
        await Assert.That(holder).Contains("return n ?? 0;");
        await Assert.That(holder).Contains("return [0, 1];");
        await Assert.That(holder).Contains("return cond ? Mode.On : Mode.Off;");
        await Assert.That(holder).DoesNotContain("undefined, undefined");
        await Assert.That(Warnings(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A type with no literal for its default (a user struct, a tuple, a
    /// <c>[StringEnum]</c> without a zero member) reports <c>MS0001</c> and
    /// keeps the placeholder.
    /// </summary>
    [Test]
    [Arguments("Point p = default; return p;", "Point")]
    [Arguments("var p = default(Point); return p;", "Point")]
    [Arguments("(int, int) t = default; return t;", "(int, int)")]
    [Arguments("Tone t = default; return t;", "Tone")]
    public async Task TypeWithoutALiteralDefault_ReportsAndKeepsThePlaceholder(
        string body,
        string type
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Types
                + $$"""

                [Transpile]
                public class Holder
                {
                    public object? Run() { {{body}} }
                }
                """
        );

        await Assert
            .That(Warnings(diagnostics))
            .Contains(message => message.Contains($"'default({type})' has no literal"));
    }

    private static IEnumerable<string> Warnings(IEnumerable<MetanoDiagnostic> diagnostics) =>
        diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).Select(d => d.Message);
}
