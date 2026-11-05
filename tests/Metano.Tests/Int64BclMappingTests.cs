using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The BCL mappings of spec 012 slice 012g (FR-035–FR-044, FR-072) in the
/// staging <see cref="Int64Representation.BigInt"/> mode: the 64-bit
/// overloads lower to the <c>metano-runtime</c> helpers, while the shipped
/// <see cref="Int64Representation.Number"/> mode never sees a <c>bigint</c>
/// template. Every golden type-checks under <c>tsgo --strict</c>.
/// </summary>
public class Int64BclMappingTests
{
    private const string UnsupportedFormat = DiagnosticCodes.UnsupportedInt64Format;

    private const string MathSource = """
        [Transpile]
        public class Statics
        {
            public long Abs(long l) => Math.Abs(l) + long.Abs(l);
            public long Max(long l, long m) => Math.Max(l, m) + long.Max(l, m);
            public long Min(long l, long m) => Math.Min(l, m) + long.Min(l, m);
            public long Widened(int i, long l) => Math.Max(i, l);
            public ulong Unsigned(ulong u, ulong v) => Math.Max(u, v) + ulong.Min(u, v);
            public int Sign(long l, ulong u) => Math.Sign(l) + long.Sign(l) + ulong.Sign(u);
            public long Clamp(long l) => Math.Clamp(l, -10, 10) + long.Clamp(l, 0, 1);
            public ulong UClamp(ulong u) => Math.Clamp(u, 1, 2);
            public (long, long) DivRem(long l, long m) => Math.DivRem(l, m);
            public (ulong, ulong) UDivRem(ulong u, ulong v) => ulong.DivRem(u, v);

            public long DivRemOut(long l, long m)
            {
                var quotient = Math.DivRem(l, m, out var rest);
                return quotient + rest;
            }

            public long BigMul(long l, long m)
            {
                var high = Math.BigMul(l, m, out long low);
                return high ^ low;
            }

            public ulong UBigMul(ulong u, ulong v)
            {
                var high = Math.BigMul(u, v, out ulong low);
                return high ^ low;
            }

            public double Pow(long l) => Math.Pow(l, 2);
            public double Doubles(double a, double b) => Math.Max(a, b) + Math.Abs(a);
        }
        """;

    private const string FormattingSource = """
        using System.Globalization;

        [Transpile]
        public class Text
        {
            public string Format(long l, ulong u, string format) =>
                l.ToString("N0") + u.ToString("X", CultureInfo.InvariantCulture) + l.ToString(format);
            public string Plain(long l) => l.ToString() + $"{l}";
            public string Holes(long l, ulong u) => $"{l:X}|{l,8}|{u,-4:D2}";
            public string NullableHole(long? l) => $"{l:N0}";
            public string IntHole(int i) => $"{i:X}";

            public long Parse(string s) =>
                long.Parse(s)
                + long.Parse(s, NumberStyles.HexNumber)
                + long.Parse(s, NumberStyles.Integer | NumberStyles.AllowThousands, null);

            public ulong UParse(string s) => ulong.Parse(s, NumberStyles.Number);
            public long TryParse(string s) => long.TryParse(s, out var x) ? x : -1;

            public ulong UTryParse(string s) =>
                ulong.TryParse(s, NumberStyles.HexNumber, null, out var x) ? x : 0;

            public int Compare(long l, long m, ulong u, int i) =>
                l.CompareTo(m) + u.CompareTo(u) + l.CompareTo(i);

            public bool Equal(long l, long m, int i, object o) =>
                l.Equals(m) && l.Equals(i) && l.Equals(o);
        }
        """;

    private const string ConvertSource = """
        [Transpile]
        public class Converts
        {
            public long ToInt64(double d, float f, decimal m, string s, bool b, char c, int i) =>
                Convert.ToInt64(d) + Convert.ToInt64(f) + Convert.ToInt64(m) + Convert.ToInt64(s)
                + Convert.ToInt64(s, 16) + Convert.ToInt64(b) + Convert.ToInt64(c) + Convert.ToInt64(i);

            public long FromWide(ulong u, object o) => Convert.ToInt64(u) + Convert.ToInt64(o);

            public ulong ToUInt64(double d, int i, long l, uint ui, string s) =>
                Convert.ToUInt64(d) + Convert.ToUInt64(i) + Convert.ToUInt64(l) + Convert.ToUInt64(ui)
                + Convert.ToUInt64(s, 2);

            public int Narrow(long l, ulong u) =>
                Convert.ToInt32(l) + Convert.ToInt16(u) + Convert.ToByte(l) + Convert.ToSByte(l)
                + Convert.ToUInt16(l) + (int)Convert.ToUInt32(u);

            public char Character(long l) => Convert.ToChar(l);
            public double Floating(long l, ulong u) => Convert.ToDouble(l) + Convert.ToSingle(u);
            public bool Truth(long l) => Convert.ToBoolean(l);
        }
        """;

    private const string UnsupportedFormatSource = """
        using System.Globalization;

        [Transpile]
        public class Unsupported
        {
            public string Currency(long l) => l.ToString("C");
            public string Custom(long l) => $"{l:#,##0}";
            public string Invalid(long l) => l.ToString("Q");
            public long Float(string s) => long.Parse(s, NumberStyles.Float);
            public string Supported(long l) => l.ToString("x8") + $"{l,5:N2}";
        }
        """;

    [Test]
    public async Task BigIntMode_MathStatics_CallTheInt64Helpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            MathSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["statics.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-math.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The shipped <c>Number</c> mode skips the <c>bigint</c> templates
    /// (<c>WhenInt64IsBigInt</c>): <c>Math.Max</c> keeps its JS rename and the
    /// statics that had no mapping stay unmapped.
    /// </summary>
    [Test]
    public async Task NumberMode_MathStatics_KeepTheShippedLowering()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            MathSource,
            Int64Representation.Number
        );

        var statics = files["statics.ts"];

        await Assert.That(statics).Contains("return Math.max(l, m) + Int64.max(l, m);");
        await Assert.That(statics).Contains("return Math.abs(l) + Int64.abs(l);");
        await Assert.That(statics).DoesNotContain("Int64(");
        await Assert.That(statics).DoesNotContain("metano-runtime");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsNotEmpty();
    }

    [Test]
    public async Task BigIntMode_FormattingParsingAndComparison_CallTheInt64Helpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            FormattingSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["text.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-formatting.ts"));

        await Assert.That(diagnostics.Where(d => d.Code == UnsupportedFormat)).IsEmpty();
    }

    [Test]
    public async Task BigIntMode_Convert_CallsTheConvertHelpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            ConvertSource,
            Int64Representation.BigInt
        );

        await Assert
            .That(files["converts.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-convert.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// MS0066 names each constant format or <c>NumberStyles</c> that is valid
    /// in .NET but outside what the runtime implements; the call is still
    /// emitted. An invalid format is not reported (it throws on .NET too).
    /// </summary>
    [Test]
    public async Task BigIntMode_UnsupportedFormatsAndStyles_ReportMS0066()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            UnsupportedFormatSource,
            Int64Representation.BigInt
        );

        var messages = diagnostics
            .Where(d => d.Code == UnsupportedFormat)
            .Select(d => d.Message)
            .ToList();

        await Assert.That(messages).Count().IsEqualTo(3);
        await Assert.That(messages[0]).StartsWith("Format 'C' is not supported");
        await Assert.That(messages[1]).StartsWith("Format '#,##0' is not supported");
        await Assert.That(messages[2]).StartsWith("NumberStyles value 167 is not supported");
        await Assert
            .That(diagnostics.Where(d => d.Code == UnsupportedFormat))
            .All(d => d.Severity == MetanoDiagnosticSeverity.Warning);
        await Assert.That(files["unsupported.ts"]).Contains("return formatInt64(l, \"C\");");
        await Assert.That(files["unsupported.ts"]).Contains("return parseInt64(s, 167);");
    }

    /// <summary>
    /// The shipped <c>Number</c> mode keeps the 2.x text: formats and
    /// alignments dropped, <c>NumberStyles</c> by name, <c>Convert</c>
    /// unmapped, no MS0066.
    /// </summary>
    [Test]
    public async Task NumberMode_FormattingAndConvert_KeepTheShippedLowering()
    {
        var formatting = TranspileHelper.TranspileWithDiagnostics(
            FormattingSource,
            Int64Representation.Number
        );

        var convert = TranspileHelper.TranspileWithDiagnostics(
            ConvertSource,
            Int64Representation.Number
        );

        var unsupported = TranspileHelper.TranspileWithDiagnostics(
            UnsupportedFormatSource,
            Int64Representation.Number
        );

        var text = formatting.Files["text.ts"];
        var diagnostics = formatting.Diagnostics.Concat(unsupported.Diagnostics);

        await Assert.That(text).Contains("return `${l}|${l}|${u}`;");
        await Assert.That(text).Contains("Int64.parse(s, NumberStyles.HexNumber)");
        await Assert.That(text).Contains("tryParseInt64(s)");
        await Assert.That(text).Contains("l.compareTo(m)");
        await Assert.That(convert.Files["converts.ts"]).Contains("Convert.toInt64(d)");
        await Assert.That(text + convert.Files["converts.ts"]).DoesNotContain("formatInt64");
        await Assert.That(text + convert.Files["converts.ts"]).DoesNotContain("convertNarrow");
        await Assert.That(diagnostics.Where(d => d.Code == UnsupportedFormat)).IsEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
