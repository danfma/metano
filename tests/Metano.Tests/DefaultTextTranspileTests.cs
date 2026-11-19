namespace Metano.Tests;

/// <summary>
/// The default text of a <c>bool</c>, <c>double</c> and <c>float</c> (baseline FR-014, #308):
/// interpolation, string concatenation, <c>ToString()</c> and <c>string.Join</c> over a value
/// of one of those static types call the runtime function that prints .NET's text; every other
/// type keeps the native conversion. The runtime functions are checked against real .NET by the
/// rows of <c>tests/BclReference</c>.
/// </summary>
public class DefaultTextTranspileTests
{
    [Test]
    public async Task Interpolation_ConvertsTheThreeTypes()
    {
        var output = TranspileText(
            """
            public static string A(bool b, double d, float f, int i) => $"{b} {d} {f} {i}";
            """
        );

        await Assert
            .That(output)
            .Contains("return `${formatBoolean(b)} ${formatDouble(d)} ${formatSingle(f)} ${i}`;");

        await Assert
            .That(output)
            .Contains(
                "import { formatBoolean, formatDouble, formatSingle } from \"metano-runtime\";"
            );
    }

    [Test]
    public async Task AlignmentOnly_PadsTheDefaultText()
    {
        var output = TranspileText(
            """
            public static string A(bool b, double d) => $"{b,8}|{d,-9}";
            """
        );

        await Assert
            .That(output)
            .Contains("alignValue(formatBoolean(b), 8)}|${alignValue(formatDouble(d), -9)}");
    }

    [Test]
    public async Task Concatenation_ConvertsEachOperandAndNeedsNoCoalesce()
    {
        var output = TranspileText(
            """
            public static string A(bool b, double? d, float f, bool? n) => "x" + b + d + f + n;
            public static string B(string s, bool b) { s += b; s += 1.5; return s; }
            """
        );

        await Assert
            .That(output)
            .Contains(
                "return \"x\" + formatBoolean(b) + formatDouble(d) + formatSingle(f) + formatBoolean(n);"
            );

        await Assert.That(output).Contains("s = (s ?? \"\") + formatBoolean(b);");
        await Assert.That(output).Contains("s = (s ?? \"\") + formatDouble(1.5);");
    }

    [Test]
    public async Task ToString_CallsTheSameFunction()
    {
        var output = TranspileText(
            """
            public static string A(bool b, double d, float f) => b.ToString() + d.ToString() + f.ToString();
            """
        );

        await Assert
            .That(output)
            .Contains("return formatBoolean(b) + formatDouble(d) + formatSingle(f);");
    }

    [Test]
    public async Task Join_ConvertsEachElementOfTheThreeTypesOnly()
    {
        var output = TranspileText(
            """
            public static string A(bool[] bs, List<double> ds, IEnumerable<float?> fs) =>
                string.Join(",", bs) + string.Join("-", ds) + string.Join(";", fs);
            public static string B(int[] ns, string[] ss) => string.Join(",", ns) + string.Join(",", ss);
            public static string C() => string.Join(",", true, 2.5);
            """
        );

        await Assert
            .That(output)
            .Contains("bs.map((item: boolean) => formatBoolean(item)).join(\",\")");

        await Assert
            .That(output)
            .Contains("[...ds].map((item: number) => formatDouble(item)).join(\"-\")");

        await Assert
            .That(output)
            .Contains("[...fs].map((item: number | null) => formatSingle(item)).join(\";\")");

        await Assert.That(output).Contains("return ns.join(\",\") + ss.join(\",\");");
        await Assert.That(output).Contains("[formatBoolean(true), formatDouble(2.5)].join(\",\")");
    }

    [Test]
    public async Task OtherTypes_KeepTheNativeConversion()
    {
        var output = TranspileText(
            """
            public static string A(int i, long l, string s, decimal m, object o) => $"{i} {l} {s} {m} {o}" + i + s;
            """
        );

        await Assert.That(output).DoesNotContain("formatBoolean");
        await Assert.That(output).DoesNotContain("formatDouble");
        await Assert.That(output).DoesNotContain("formatSingle");
    }

    [Test]
    public async Task FormatClauses_KeepTheirFormatters()
    {
        var output = TranspileText(
            """
            public static string A(double d) => $"{d:F2} {d:G}" + d.ToString("F1");
            """
        );

        await Assert.That(output).Contains("formatDouble(d, \"F2\")");
        await Assert.That(output).Contains("formatDouble(d, \"G\")");
        await Assert.That(output).Contains("formatDouble(d, \"F1\")");
    }

    [Test]
    public async Task DecimalCastToBinaryFloatingPoint_ReadsTheNumberBeforeFormatting()
    {
        var output = TranspileText(
            """
            public static string A(decimal m, decimal? n)
            {
                double d = (double)m;
                return "v" + (double)m + "|" + (float)m + "|" + (d + 1) + "|" + (double?)n;
            }
            """
        );

        await Assert.That(output).Contains("const d = m.toNumber();");

        await Assert
            .That(output)
            .Contains(
                "return \"v\" + formatDouble(m.toNumber()) + \"|\" + formatSingle(Math.fround(m.toNumber())) + \"|\" + formatDouble(d + 1) + \"|\" + formatDouble(n == null ? null : n.toNumber());"
            );
    }

    /// <summary>
    /// A <c>long?</c> read as the <c>double</c> result of <c>??</c> is
    /// converted before the fallback applies, and a <c>char</c> read as a
    /// <c>double</c> is its UTF-16 code: <c>formatDouble</c> receives a number.
    /// </summary>
    [Test]
    public async Task ImplicitConversionsToDouble_ReachTheFormatterAsNumbers()
    {
        var output = TranspileText(
            """
            public static string Coalesce(long? k, long x)
            {
                double e = k ?? 1.5;
                double f = k ?? x;
                return "v" + e + f;
            }

            public static string Chars(char c)
            {
                double d = c;
                double a = 'a';
                return "v" + d + a;
            }
            """
        );

        await Assert.That(output).Contains("const e = (k == null ? null : Number(k)) ?? 1.5;");
        await Assert.That(output).Contains("const f = Number(k ?? x);");
        await Assert.That(output).Contains("const d = c.charCodeAt(0);");
        await Assert.That(output).Contains("const a = 97;");
    }

    [Test]
    public async Task ErasedValues_UseTheObjectMappingWithoutLosingNullConcatenation()
    {
        var output = TranspileText(
            """
            public static string Boxed(object value) => $"{value}" + value.ToString();
            public static string Generic<T>(T value) => $"{value}" + value.ToString();
            """
        );

        await Assert.That(output).Contains("${objectText(value ?? \"\")}");
        await Assert.That(output).Contains("objectText(value)");
    }

    [Test]
    public async Task NullableToString_UsesTheUnderlyingFormatterOrNullableMapping()
    {
        var output = TranspileText(
            """
            public static string A(bool? b, double? d, int? i) => b.ToString() + d.ToString() + i.ToString();
            """
        );

        await Assert.That(output).Contains("formatBoolean(b) ?? \"\"");
        await Assert.That(output).Contains("formatDouble(d) ?? \"\"");
        await Assert.That(output).Contains("formatText(i) ?? \"\"");
    }

    [Test]
    public async Task TupleAndTimeSpan_UseDeclarativeDefaultTextMappings()
    {
        var output = TranspileText(
            """
            public static string A(System.TimeSpan span) => $"{span}" + (1, "a").ToString();
            """
        );

        await Assert.That(output).Contains("${formatTimeSpan(span)}");
        await Assert.That(output).Contains("formatTuple([1, \"a\"] as [number, string])");
    }

    [Test]
    public async Task EnumDefaultText_UsesItsDescriptorButDFormatRemainsNumeric()
    {
        var files = TranspileHelper.Transpile(
            """
            [Transpile] public enum Color { Red, Green }
            [Transpile] public static class Text
            {
                public static string A(Color value) => $"{value}|{value:D}" + value.ToString();
            }
            """
        );

        await Assert
            .That(files["text.ts"])
            .Contains("${enumText(value, Color$runtimeType)}|${value}");

        await Assert.That(files["text.ts"]).Contains("enumText(value, Color$runtimeType)");
        await Assert.That(files["color.ts"]).Contains("Color$runtimeType");
    }

    [Test]
    public async Task BclEnumDefaultText_DoesNotRequestUnavailableDescriptors()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;
            [Transpile] public static class Text
            {
                public static string A(DayOfWeek day, StringComparison comparison) =>
                    $"{day}|{comparison}" + day.ToString() + comparison.ToString();
                public static string B(DayOfWeek[] days) => string.Join(";", days);
            }
            """
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(files["text.ts"]).DoesNotContain("$runtimeType");
        await Assert.That(files["text.ts"]).DoesNotContain("enumText");
    }

    [Test]
    public async Task DecimalScale_UsesMetadataOnlyAtTrailingZeroLiteralAndParseSites()
    {
        var output = TranspileText(
            """
            public static string A() => 2.50m + "|" + 1.25m + "|" + decimal.Parse("2.500").ToString();
            """
        );

        await Assert.That(output).Contains("withDecimalScale(new Decimal(\"2.50\"), 2)");
        await Assert.That(output).DoesNotContain("withDecimalScale(new Decimal(\"1.25\")");
        await Assert.That(output).Contains("parseDecimal(\"2.500\", Decimal)");
        await Assert.That(output).Contains("formatText");
    }

    private static string TranspileText(string members) =>
        TranspileHelper.Transpile(
            $$"""
            using System.Collections.Generic;

            [Transpile]
            public static class Text
            {
            {{members}}
            }
            """
        )["text.ts"];
}
