using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The BCL mappings of spec 012 slice 012g (FR-035–FR-044, FR-072): the
/// 64-bit overloads lower to the <c>metano-runtime</c> helpers. Every golden
/// type-checks under <c>tsgo --strict</c>.
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
            public string Plain(long l) => l.ToString() + $"{l}" + l.ToString(CultureInfo.InvariantCulture);
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
            public int IntStyles(string s) => int.Parse(s, NumberStyles.HexNumber);
        }
        """;

    private const string CollectionSource = """
        using System.Collections.Generic;
        using System.Linq;

        [Transpile]
        public record Item(long Size, long? Optional, int Count, ulong Flags);

        [Transpile]
        public class Collections
        {
            private int _cursor;

            public long[] Slots = new long[4];

            public int Next() => _cursor++;
            public int Index(int[] values, long l, ulong u) => values[l] + values[u];

            public void Write(long[] values, long l)
            {
                values[l] = 1;
                values[l] += 2;
                values[Next()] *= 3;
                Slots[l]++;
            }

            public long Length(int[] values) => values.LongLength;
            public long[] Zeros(int n) => new long[n];
            public int[] ByLongLength(long n) => new int[n];
            public string[] Strings(uint n) => new string[n];
            public bool[] Flags(int n) => new bool[n];
            public Item[] Items(int n) => new Item[n];
            public long Count(List<int> xs) => xs.LongCount() + xs.LongCount(x => x > 1);
            public int IntCount(List<int> xs) => xs.Count();

            public long Sum(long[] ids, List<Item> items) => ids.Sum() + items.Sum(x => x.Size);
            public long? NullableSum(List<Item> items) => items.Sum(x => x.Optional);

            public double Average(long[] ids, List<long?> optional, List<Item> items) =>
                ids.Average() + (optional.Average() ?? 0) + items.Average(x => x.Size);

            public long Extremes(long[] ids, List<Item> items, ulong[] flags) =>
                ids.Max() + items.Min(x => x.Size) + (items.Max(x => x.Optional) ?? 0)
                + (long)flags.Min();

            public int IntSum(int[] xs) => xs.Sum();
        }
        """;

    private const string TicksSource = """
        [Transpile]
        public class Clock
        {
            public long Ticks(TimeSpan span, DateTime moment) =>
                span.Ticks + moment.Ticks + TimeSpan.TicksPerSecond;

            public TimeSpan Span(long ticks) => TimeSpan.FromTicks(ticks);
            public TimeSpan Constructed(long ticks) => new TimeSpan(ticks);
            public DateTime Moment(long ticks) => new DateTime(ticks);
            public DateTime Parts() => new DateTime(2026, 1, 1);

            public void Print(long l, ulong u, int i)
            {
                Console.WriteLine(l);
                Console.WriteLine(u);
                Console.WriteLine(i);
            }
        }
        """;

    private const string ExpressionTreeSource = """
        using System.Collections.Generic;
        using System.Linq;

        [Transpile]
        public record Row(long Size, int Count, double Weight, long? Optional);

        [Transpile]
        public class Rows
        {
            public List<Row> Filter(IQueryable<Row> rows, long cap, int limit) =>
                rows.Where(r =>
                        r.Size > cap
                        && r.Count < 5L
                        && (long)r.Count == 2
                        && r.Weight > cap
                        && r.Size < limit
                        && r.Optional + r.Count > 10
                    )
                    .ToList();
        }
        """;

    private const string BigIntegerSource = """
        #nullable enable
        using System.Numerics;

        [Transpile]
        public class Big
        {
            public decimal ToDecimal(BigInteger b) => (decimal)b;
            public BigInteger FromDecimal(decimal d) => (BigInteger)d;
            public int ToInt(BigInteger b) => (int)b;
            public uint ToUInt(BigInteger b) => (uint)b;
            public short ToShort(BigInteger b) => (short)b;
            public byte ToByte(BigInteger b) => unchecked((byte)b);
            public char ToChar(BigInteger b) => (char)b;
            public double ToDouble(BigInteger b) => (double)b;
            public float ToSingle(BigInteger b) => (float)b;
            public BigInteger FromDouble(double d) => (BigInteger)d;
            public BigInteger FromInt(int i) => i;
            public long ToLong(BigInteger b) => (long)b;
            public int? Lifted(BigInteger? b) => (int?)b;
            public int Unwrapped(BigInteger? b) => (int)b;
        }
        """;

    [Test]
    public async Task MathStatics_CallTheInt64Helpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(MathSource);

        await Assert
            .That(files["statics.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-math.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The <c>nint</c> / <c>nuint</c> statics (DECIDED-2: nint follows every
    /// rule of the spec) bind the <c>IntPtr</c> / <c>UIntPtr</c> overloads,
    /// whose stable type names never match the <c>long</c> templates; the
    /// pointer-width variants route them to the same runtime helpers.
    /// </summary>
    [Test]
    public async Task NativeIntegerMathStatics_CallTheInt64Helpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System;

            [Transpile]
            public static class NativeMath
            {
                public static nint Abs(nint v) => Math.Abs(v);
                public static nint Max(nint a, nint b) => Math.Max(a, b);
                public static nint Min(nint a, nint b) => Math.Min(a, b);
                public static int Sign(nint v) => Math.Sign(v);
                public static nint Clamp(nint v, nint lo, nint hi) => Math.Clamp(v, lo, hi);
                public static nuint MaxU(nuint a, nuint b) => Math.Max(a, b);
                public static nuint ClampU(nuint v, nuint lo, nuint hi) => Math.Clamp(v, lo, hi);
            }
            """
        );

        var native = files["native-math.ts"];

        await Assert.That(native).Contains("return absInt64(v);");
        await Assert.That(native).Contains("return maxInt64(a, b);");
        await Assert.That(native).Contains("return minInt64(a, b);");
        await Assert.That(native).Contains("return signInt64(v);");
        await Assert.That(native).Contains("return clampInt64(v, lo, hi);");
        await Assert.That(native).Contains("return maxUInt64(a, b);");
        await Assert.That(native).Contains("return clampUInt64(v, lo, hi);");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task FormattingParsingAndComparison_CallTheInt64Helpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(FormattingSource);

        await Assert
            .That(files["text.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-formatting.ts"));

        await Assert.That(diagnostics.Where(d => d.Code == UnsupportedFormat)).IsEmpty();
    }

    [Test]
    public async Task Convert_CallsTheConvertHelpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ConvertSource);

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
    public async Task UnsupportedFormatsAndStyles_ReportMS0066()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            UnsupportedFormatSource
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

        await Assert
            .That(files["unsupported.ts"])
            .Contains("Int32.parse(s, NumberStyles.HexNumber)");
    }

    [Test]
    public async Task ArraysAndLinq_IndexByNumberAndCallTheInt64Terminals()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(CollectionSource);

        await Assert
            .That(files["collections.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-collections.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task TicksAndConsole_CallTheTicksHelpersAndString()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(TicksSource);

        await Assert
            .That(files["clock.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-ticks-console.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A queryable lambda carries <c>bigint</c> literals and captures typed
    /// <c>System.Int64</c>, and a <c>convert</c> node for every conversion
    /// across a 64-bit integer, explicit or implicit (FR-044).
    /// </summary>
    [Test]
    public async Task ExpressionTree_CarriesBigIntLiteralsAndConvertNodes()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ExpressionTreeSource);

        await Assert
            .That(files["rows.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-expression-tree.ts"));

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();
    }

    /// <summary>
    /// <c>Min</c> / <c>Max</c> with an <c>IComparer</c> order by it, which no
    /// runtime terminal does: the call reports the ignored comparer and lowers
    /// to the throwing placeholder instead of a terminal (#648).
    /// </summary>
    [Test]
    public async Task MaxWithAComparer_ReportsTheIgnoredComparer()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            """
            using System.Collections.Generic;
            using System.Linq;

            [Transpile]
            public class Ordered
            {
                public long Highest(long[] ids, IComparer<long> order) => ids.Max(order);
            }
            """
        );

        await Assert
            .That(diagnostics.Select(diagnostic => diagnostic.Message))
            .Contains(
                "The comparer of 'Max' is not supported; the default comparison would be used. "
                    + "The call throws at run time."
            );

        await Assert
            .That(files["ordered.ts"])
            .Contains("throw new Error(\"Unsupported expression: UnsupportedLinqOverload\");");
    }

    /// <summary>
    /// Dart, which always lowers 64-bit integers exactly, fills a sized
    /// array with the element default; its 64-bit <c>int</c> takes a
    /// <c>long</c> length as is. A <c>null</c> default stays unsupported
    /// (sound null safety), and only a 64-bit parser's <c>NumberStyles</c>
    /// folds.
    /// </summary>
    [Test]
    public async Task Dart_SizedArray_FillsTheElementDefault()
    {
        var (files, _) = TranspileHelper.TranspileDart(
            """
            using Metano.Annotations;

            [Transpile]
            public class Buffers
            {
                public long[] Zeros(long n) => new long[n];
                public bool[] Flags(int n) => new bool[n];
                public string[] Names(int n) => new string[n];
                public int Hex(string s) => int.Parse(s, System.Globalization.NumberStyles.HexNumber);
            }
            """
        );

        var output = files["buffers.dart"];

        await Assert.That(output).Contains("List<int>.filled(n, 0)");
        await Assert.That(output).Contains("List<bool>.filled(n, false)");
        await Assert.That(output).DoesNotContain("filled(n, null)");
        await Assert.That(output).Contains("NumberStyles.hexNumber");
        await Assert.That(output).Contains("Unsupported expression: ArrayCreationExpression");
    }

    /// <summary>
    /// Every <c>BigInteger</c> cast is an <c>IrNumericConversion</c> (FR-003),
    /// checked in any context with .NET's messages.
    /// </summary>
    [Test]
    public async Task BigIntegerCasts_CallTheCheckedHelpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(BigIntegerSource);

        await Assert
            .That(files["big.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-big-integer.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
