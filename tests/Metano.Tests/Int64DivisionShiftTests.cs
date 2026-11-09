using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;

namespace Metano.Tests;

/// <summary>
/// The 64-bit division, remainder and shifts of spec 012 slice 012d
/// (FR-027–FR-030) in the staging <see cref="Int64Representation.BigInt"/>
/// mode: <c>/</c> and <c>%</c> call the throwing helpers unless the divisor
/// is a constant that cannot throw, shift counts are masked with <c>63</c>
/// and converted to <c>bigint</c>, <c>&lt;&lt;</c> wraps in either context
/// and <c>&gt;&gt;&gt;</c> on <c>long</c> shifts the unsigned bits. Every
/// golden type-checks under <c>tsgo --strict</c>.
/// </summary>
public class Int64DivisionShiftTests
{
    private const string DivisionSource = """
        [Transpile]
        public class Divide
        {
            private int _cursor;

            public long Total { get; set; }
            public long[] Slots = new long[] { 1, 2, 3 };

            public int Next() => _cursor++;

            public long Div(long l, long m) => l / m;
            public long Rem(long l, long m) => l % m;
            public ulong UDiv(ulong u, ulong v) => u / v;
            public ulong URem(ulong u, ulong v) => u % v;
            public long DivConst(long l) => l / 7;
            public long RemConst(long l) => l % 7;
            public long DivMinusOne(long l) => l / -1;
            public long RemMinusOne(long l) => l % -1;
            public ulong UDivConst(ulong u) => u / 3;
            public ulong URemMax(ulong u) => u % ulong.MaxValue;
            public long ConstDividend(long m) => 100 / m;
            public long DivInt(long l, int i) => l / i;
            public long Checked(long l, long m) => checked(l / m + l % m);
            public long Mixed(long l, long m) => l / m * 2 + l % m;
            public nint Native(nint a, nint b) => a / b;
            public long? Lifted(long? nl, long? nm) => nl / nm;
            public long? LiftedRem(long? nl, long m) => nl % m;
            public int Narrow(int a, int b) => a / b + a % b;

            public long Compound(long l, long m)
            {
                l /= m;
                l %= m;
                l /= 4;
                l %= 3;
                return l;
            }

            public ulong UCompound(ulong u, ulong v)
            {
                u /= v;
                u %= v;
                return u;
            }

            public long? LiftedCompound(long? nl, long m)
            {
                nl /= m;
                nl %= m;
                return nl;
            }

            public void Members(long m)
            {
                Total /= m;
                Slots[Next()] %= m;
            }
        }
        """;

    private const string ShiftSource = """
        [Transpile]
        public class Shift
        {
            private int _cursor;

            public long Total { get; set; }
            public long[] Slots = new long[] { 1, 2, 3 };

            public int Next() => _cursor++;

            public long Left(long l, int n) => l << n;
            public long LeftConst(long l) => l << 3;
            public long LeftMasked(long l) => l << 70;
            public long LeftNegative(long l) => l << -1;
            public ulong ULeft(ulong u, int n) => u << n;
            public long Right(long l, int n) => l >> n;
            public ulong URight(ulong u, int n) => u >> n;
            public long RightConst(long l) => l >> 64;
            public long Unsigned(long l) => l >>> 2;
            public long UnsignedVar(long l, int n) => l >>> n;
            public ulong UUnsigned(ulong u) => u >>> 2;
            public ulong UUnsignedVar(ulong u, int n) => u >>> n;
            public long CheckedLeft(long l) => checked(l << 1);
            public long ByteCount(long l, byte b) => l << b;
            public long Expression(long l, int n) => l << n + 1;
            public long? Lifted(long? nl, int n) => nl << n;
            public long? LiftedCount(long l, int? n) => l << n;
            public ulong? ULiftedUnsigned(ulong? nu, int n) => nu >>> n;
            public long Bitwise(long l, long m) => (l & m) | (l ^ ~m);
            public int Narrow(int i, int n) => i << n | i >> 2 | i >>> n;

            public long Compound(long l, int n)
            {
                l <<= n;
                l >>= 2;
                l >>>= n;
                return l;
            }

            public ulong UCompound(ulong u, int n)
            {
                u <<= 1;
                u >>= n;
                u >>>= 3;
                return u;
            }

            public long CheckedCompound(long l)
            {
                checked
                {
                    l <<= 1;
                }

                return l;
            }

            public void Members(int n)
            {
                Total <<= 2;
                Slots[Next()] >>>= n;
            }
        }
        """;

    [Test]
    public async Task BigIntMode_DivisionAndRemainder_CallTheThrowingHelpers()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(DivisionSource);

        await Assert
            .That(files["divide.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-division.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task BigIntMode_Shifts_MaskAndConvertTheCount()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ShiftSource);

        await Assert
            .That(files["shift.ts"])
            .IsEqualTo(TranspileHelper.ReadExpected("int64-shifts.ts"));

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The shipped <c>Number</c> mode keeps the 2.x output (DECIDED-22):
    /// <c>intDiv</c>, the native remainder and shifts, no count mask and the
    /// native compound operators.
    /// </summary>
    [Test]
    public async Task NumberMode_KeepsTheShippedDivisionAndShifts()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(DivisionSource + ShiftSource);

        var divide = files["divide.ts"];
        var shift = files["shift.ts"];

        await Assert.That(divide).Contains("return intDiv(l, m);");
        await Assert.That(divide).Contains("return l % m;");
        await Assert.That(divide).Contains("l %= m;");
        await Assert.That(shift).Contains("return l << n;");
        await Assert.That(shift).Contains("return l << 70;");
        await Assert.That(shift).Contains("l >>>= n;");
        await Assert.That(divide + shift).DoesNotContain("divInt64");
        await Assert.That(divide + shift).DoesNotContain("& 63");
        await Assert.That(divide + shift).DoesNotContain("asIntN");
    }

    /// <summary>
    /// Dart's <c>int</c> is 64-bit, so the core's masked count is all a
    /// shift needs (a count of 64 or more would otherwise shift every bit
    /// out); <c>&gt;&gt;&gt;</c> is Dart's own logical shift, and the
    /// reduction prints as its operand (DECIDED-19).
    /// </summary>
    [Test]
    public async Task Dart_MasksTheShiftCount()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(ShiftSource);

        var shift = files["shift.dart"];

        await Assert.That(shift).Contains("int left(int l, int n) => l << (n & 63);");
        await Assert.That(shift).Contains("int leftMasked(int l) => l << 6;");
        await Assert.That(shift).Contains("int unsigned(int l) => l >>> 2;");
        await Assert.That(shift).Contains("int narrow(int i, int n) => i << n | i >> 2 | i >>> n;");
        await Assert.That(shift).Contains("l = l << (n & 63);");
        await Assert.That(shift).Contains("l = l >>> (n & 63);");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private static List<MetanoDiagnostic> UnsupportedFeatures(
        IEnumerable<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature).ToList();
}
