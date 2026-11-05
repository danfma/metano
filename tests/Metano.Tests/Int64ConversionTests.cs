using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The 64-bit type mapping, literals, constants and conversions of spec 012
/// slice 012b (FR-001–FR-020, FR-031–FR-034, FR-058, FR-069) in the staging
/// <see cref="Int64Representation.BigInt"/> mode, plus the floating-point to
/// narrow-integer truncation of FR-070, which applies in both modes.
/// </summary>
public class Int64ConversionTests
{
    /// <summary>
    /// Every row of contracts/ts-emission.md § Conversions and § Literals, at
    /// every position Roslyn reports an implicit conversion (research R3).
    /// The golden type-checks under <c>tsgo --strict</c>.
    /// </summary>
    private const string ConversionSource = """
        using System;
        using System.Numerics;
        using System.Collections.Generic;

        [Transpile] public enum Color { Red = 1, Green = 2 }
        [Transpile] public enum Big : long { A = 1, B = 1L << 40, C = (1L << 53) + 1 }
        [Transpile, Flags] public enum Mask : ulong { None = 0, Low = 1, High = 1UL << 40 }

        [Transpile]
        public class Conv
        {
            public const long Huge = 9007199254740993L;
            public const ulong UMax = ulong.MaxValue;
            public const int Small = 5;
            public long Field;
            public ulong UField;
            public long? NField;
            public long Initialized = 7;

            public void Take(long v) { }
            public long Widen(int i) => i;
            public long WidenU(uint ui) => ui;
            public long WidenC(char c) => c;
            public long WidenS(short s) => s;
            public ulong WidenB(byte b) => b;
            public long Arg(int i) { Take(i); return 0; }
            public long Cond(bool f, int i, long l) => f ? i : l;
            public List<long> ListInit(int i) => new List<long> { 1, i };
            public long[] ArrInit(int i) => new long[] { 1, i };
            public Func<long> Lambda(int i) => () => i;
            public long Compound(long l, int i) { l += i; Field += i; return l; }
            public long Add(long l, int i) => l + i;
            public int ToInt(long l) => (int)l;
            public uint ToUInt(long l) => (uint)l;
            public short ToShort(long l) => (short)l;
            public ushort ToUShort(long l) => (ushort)l;
            public sbyte ToSByte(long l) => (sbyte)l;
            public byte ToByte(long l) => (byte)l;
            public char ToChar(long l) => (char)l;
            public int ToIntChecked(long l) => checked((int)l);
            public char ToCharChecked(long l) => checked((char)l);
            public ulong ToULong(long l) => (ulong)l;
            public long FromULong(ulong u) => (long)u;
            public ulong ToULongChecked(long l) => checked((ulong)l);
            public long FromULongChecked(ulong u) => checked((long)u);
            public double ToDouble(long l) => l;
            public double ToDoubleCast(long l) => (double)l;
            public float ToSingle(long l) => (float)l;
            public float ToSingleImplicit(long l) => l;
            public long FromDouble(double d) => (long)d;
            public long FromFloat(float f) => (long)f;
            public long FromDoubleChecked(double d) => checked((long)d);
            public ulong UFromDouble(double d) => (ulong)d;
            public ulong UFromDoubleChecked(double d) => checked((ulong)d);
            public int IntFromDouble(double d) => (int)d;
            public uint UIntFromDouble(double d) => (uint)d;
            public int IntFromDoubleChecked(double d) => checked((int)d);
            public byte ByteFromDouble(double d) => (byte)d;
            public sbyte SByteFromDouble(double d) => (sbyte)d;
            public short ShortFromDouble(double d) => (short)d;
            public ushort UShortFromDouble(double d) => (ushort)d;
            public char CharFromDouble(double d) => (char)d;
            public byte ByteFromDoubleChecked(double d) => checked((byte)d);
            public long LongFromIntOfDouble(double d) => (int)d;
            public int ConstCast() => (int)3.7;
            public decimal ToDecimal(long l) => l;
            public decimal ToDecimalCast(long l) => (decimal)l;
            public long FromDecimal(decimal m) => (long)m;
            public ulong UFromDecimal(decimal m) => (ulong)m;
            public BigInteger ToBig(long l) => l;
            public long FromBig(BigInteger b) => (long)b;
            public ulong UFromBig(BigInteger b) => (ulong)b;
            public long FromEnum(Color e) => (long)e;
            public Color ToEnum(long l) => (Color)l;
            public Big ToBigEnum(long l) => (Big)l;
            public long FromBigEnum(Big e) => (long)e;
            public long? LiftId(int? ni) => ni;
            public long? LiftCall() => F();
            public int? NarrowLift(long? nl) => (int?)nl;
            public int? F() => 1;
            public bool Eq(long l, int i) => l == i;
            public bool EqConst(long l) => l != 5;
            public bool Rel(long l, int i, uint ui) => l < i && l >= ui;
            public bool RelU(uint ui, int i) => ui < i;
            public long Promote(uint ui, int i) => ui + i;
            public int PromoteNarrow(uint ui, int i) => (int)(ui + i);
            public bool Pat(long l) => l is 0 or > 10;
            public string Sw(long l) => l switch { 5 => "five", _ => "other" };
            public string SwStmt(long l) { switch (l) { case 1L << 40: return "big"; case 3: return "three"; default: return "x"; } }
            public string Str(long l) => "id=" + l + $"{l}" + l.ToString();
            public long Plus(long l) => +l;
            public long Max() => long.MaxValue;
            public long Min() => long.MinValue;
            public ulong UMaxV() => ulong.MaxValue;
            public long Shifted() => 1L << 40;
            public long Unch() => unchecked((long)ulong.MaxValue);
            public long Hex() => 0xFFL;
            public ulong UHex() => 0xFFFF_FFFF_FFFF_FFFFUL;
            public long Def() => default;
            public long DefT() => default(long);
            public long Opt(long x = 5) => x;
            public long RefConst() => Huge;
            public long IntConst() => Small;
            public nint NMax() => nint.MaxValue;
            public nint NMin() => nint.MinValue;
            public nuint NUMax() => nuint.MaxValue;
            public int PtrSize() => IntPtr.Size;
            public nint NWiden(int i) => i;
            public double MixedRel(long l, double d) => l < d ? 1 : 0;
            public long UncheckedInt() { long x = unchecked((int)0xFFFFFFFF); return x; }
        }
        """;

    /// <summary>The <see cref="ConversionSource"/> output in BigInt mode, shared by the tests that read it.</summary>
    private static readonly Lazy<(
        Dictionary<string, string> Files,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    )> BigIntOutput = new(() =>
        TranspileHelper.TranspileWithDiagnostics(ConversionSource, Int64Representation.BigInt)
    );

    [Test]
    public async Task BigIntMode_ConversionMatrix_MatchesGolden()
    {
        var (files, _) = BigIntOutput.Value;

        await Assert
            .That(files["conv.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-conversions.ts"));
    }

    [Test]
    public async Task BigIntMode_LongBackedEnums_KeepNumberValues()
    {
        var (files, _) = BigIntOutput.Value;

        await Assert.That(files["big.ts"]).Contains("B = 1099511627776,");
        await Assert.That(files["big.ts"]).Contains("C = 9007199254740992,");
    }

    [Test]
    public async Task BigIntMode_Record_MapsEverySixtyFourBitPosition()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            "[Transpile] public record Snowflake(long Id, ulong Mask, long? Parent, long[] Ids);",
            Int64Representation.BigInt
        );

        await Assert
            .That(files["snowflake.ts"])
            .Contains(
                "constructor(readonly id: bigint, readonly mask: bigint, "
                    + "readonly parent: bigint | null, readonly ids: bigint[])"
            );
    }

    /// <summary>
    /// A constant folded inside a checked context takes the kind of its
    /// converted type, so a 32-bit constant read as <c>long</c> is a
    /// <c>bigint</c> literal (the 012a <c>TODO(012b)</c>).
    /// </summary>
    [Test]
    public async Task BigIntMode_CheckedContextConstant_TakesTheConvertedKind()
    {
        var (files, _) = BigIntOutput.Value;

        await Assert.That(files["conv.ts"]).Contains("const x = -1n;");
    }

    [Test]
    public async Task Int64EnumValuePrecision_ReportsTheInexactMember()
    {
        var (_, diagnostics) = BigIntOutput.Value;

        var reports = diagnostics
            .Where(d => d.Code == DiagnosticCodes.Int64EnumValuePrecision)
            .ToList();

        await Assert.That(reports).Count().IsEqualTo(2);
        await Assert
            .That(reports.All(d => d.Severity == MetanoDiagnosticSeverity.Warning))
            .IsTrue();

        var precision = reports.Single(d => d.Message.Contains("'Big.C'"));

        await Assert.That(precision.Message).Contains("it is emitted as 9007199254740992");
        await Assert.That(LocatedText(precision)).IsEqualTo("C");
    }

    [Test]
    public async Task Int64EnumValuePrecision_ReportsALargeFlagsMember()
    {
        var (_, diagnostics) = BigIntOutput.Value;

        var flags = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.Int64EnumValuePrecision && d.Message.Contains("'Mask.High'")
        );

        await Assert.That(flags.Message).Contains("beyond the 32 bits");
        await Assert.That(LocatedText(flags)).IsEqualTo("High");
    }

    [Test]
    public async Task Int64EnumValuePrecision_IgnoresExactAndInt32Members()
    {
        const string source = """
            [Transpile] public enum Exact : long { A = 1L << 53, B = -(1L << 60) }
            [Transpile, System.Flags] public enum Small : long { A = 1, B = 1L << 30 }
            [Transpile, System.Flags] public enum Narrow { A = 1, B = 1 << 30 }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            source,
            Int64Representation.BigInt
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.Int64EnumValuePrecision))
            .IsFalse();
    }

    /// <summary>
    /// Dart keeps <c>int</c> and prints only the conversions that change the
    /// value (DECIDED-19): narrowing keeps the low bits, floating point
    /// truncates, a lifted one short-circuits on <c>null</c>.
    /// </summary>
    [Test]
    public async Task Dart_PrintsTheNarrowingConversions()
    {
        var (files, _) = DartTranspileHelper.Transpile(ConversionSource);

        var output = files["conv.dart"];

        await Assert.That(output).Contains("int toInt(int l) => l.toSigned(32);");
        await Assert.That(output).Contains("int toByte(int l) => l.toUnsigned(8);");
        await Assert.That(output).Contains("int widen(int i) => i;");
        await Assert.That(output).Contains("int fromULong(int u) => u as int;");
        await Assert.That(output).Contains("String toChar(int l) => l as String;");
        await Assert.That(output).Contains("double toDouble(int l) => l.toDouble();");
        await Assert.That(output).Contains("int fromDouble(double d) => d.toInt();");
        await Assert
            .That(output)
            .Contains("int byteFromDouble(double d) => d.toInt().toUnsigned(8);");
        await Assert.That(output).Contains("int? narrowLift(int? nl) => nl?.toSigned(32);");
    }

    /// <summary>
    /// <c>MS0064</c> describes the BigInt-mode emission, so the shipped
    /// <c>Number</c> mode and Dart (whose <c>int</c> is 64-bit) report none.
    /// </summary>
    [Test]
    public async Task Int64EnumValuePrecision_IsSilentInNumberModeAndDart()
    {
        var (_, numberDiagnostics) = TranspileHelper.TranspileWithDiagnostics(
            ConversionSource,
            Int64Representation.Number
        );

        var (_, dartDiagnostics) = DartTranspileHelper.Transpile(ConversionSource);

        await Assert
            .That(numberDiagnostics.Any(d => d.Code == DiagnosticCodes.Int64EnumValuePrecision))
            .IsFalse();
        await Assert
            .That(dartDiagnostics.Any(d => d.Code == DiagnosticCodes.Int64EnumValuePrecision))
            .IsFalse();
    }

    /// <summary>
    /// A user function named like a conversion helper is not mistaken for it:
    /// only the calls the bridge emits import from <c>metano-runtime</c>.
    /// </summary>
    [Test]
    public async Task NumberMode_UserFunctionNamedLikeAHelper_IsNotImported()
    {
        const string source = """
            [Transpile, ExportedAsModule]
            public static class Helpers
            {
                public static int MapNullable(int x) => x;
                public static int Int32FromDouble(int x) => MapNullable(x);
            }
            """;

        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            source,
            Int64Representation.Number
        );

        await Assert.That(files["helpers.ts"]).DoesNotContain("metano-runtime");
    }

    private const string ForEachSource = """
        [Transpile]
        public class Loops
        {
            public long Sum(int[] ints)
            {
                long total = 0;
                foreach (long x in ints)
                    total += x;
                return total;
            }

            public int Narrow(long[] longs)
            {
                var count = 0;
                foreach (int x in longs)
                    count += x;
                return count;
            }
        }
        """;

    /// <summary>
    /// A <c>foreach</c> whose variable converts each element declares the
    /// variable as that conversion (FR-010), so a <c>bigint</c> loop variable
    /// never holds a <c>number</c>.
    /// </summary>
    [Test]
    public async Task BigIntMode_ForEach_ConvertsEachElement()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            ForEachSource,
            Int64Representation.BigInt
        );

        var output = files["loops.ts"];

        await Assert.That(output).Contains("for (const x$element of ints) {");
        await Assert.That(output).Contains("const x = BigInt(x$element);");
        await Assert.That(output).Contains("for (const x$element of longs) {");
        await Assert.That(output).Contains("const x = Number(BigInt.asIntN(32, x$element));");
    }

    [Test]
    public async Task NumberMode_ForEach_KeepsTheShippedLoop()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(
            ForEachSource,
            Int64Representation.Number
        );

        await Assert.That(files["loops.ts"]).Contains("for (const x of ints) {");
        await Assert.That(files["loops.ts"]).DoesNotContain("$element");
    }

    private static string LocatedText(MetanoDiagnostic diagnostic) =>
        diagnostic.Location!.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
}
