using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The integers of 32 bits or fewer (spec 012 FR-074–FR-077, issue #639):
/// <c>sbyte</c>, <c>byte</c>, <c>short</c>, <c>ushort</c>, <c>int</c> and
/// <c>uint</c> stay a JavaScript <c>number</c>, so a narrowing conversion
/// keeps the low bits with the native 32-bit operators, a compound assignment
/// or an increment on a type narrower than <c>int</c> stores the narrowed
/// value, a <c>uint</c> bitwise result is read back as unsigned, and a
/// checked context range-checks conversions and <c>int</c> / <c>uint</c>
/// arithmetic. Unchecked <c>int</c> / <c>uint</c> arithmetic is not wrapped
/// (the documented gap). Every golden type-checks under <c>tsgo --strict</c>.
/// </summary>
public class NarrowIntegerLoweringTests
{
    private const string ConversionsSource = """
        #nullable enable

        [Transpile]
        public class Narrowing
        {
            public uint IntToUInt(int x) => (uint)x;
            public int UIntToInt(uint x) => (int)x;
            public short IntToShort(int x) => (short)x;
            public ushort IntToUShort(int x) => (ushort)x;
            public sbyte IntToSByte(int x) => (sbyte)x;
            public byte IntToByte(int x) => (byte)x;
            public byte SByteToByte(sbyte x) => (byte)x;
            public sbyte ByteToSByte(byte x) => (sbyte)x;
            public ushort ShortToUShort(short x) => (ushort)x;
            public uint SByteToUInt(sbyte x) => (uint)x;
            public byte Sum(byte a, byte b) => (byte)(a + b);
            public short Negated(short x) => (short)-x;
            public int Widened(byte x) => (int)x;
            public uint WidenedUnsigned(ushort x) => (uint)x;
            public int Same(int x) => (int)x;
            public short WidenedSigned(byte x) => (short)x;
            public byte Masked(int x) => (byte)(x & 0xFF);
            public ushort MaskedPair(uint x, uint y) => (ushort)(x & y & 0xFFFF);
            public uint Constant() => unchecked((uint)-1);
            public byte ConstantByte() => unchecked((byte)300);
            public byte FromDouble(double d) => (byte)d;
            public short FromFloat(float f) => (short)f;
            public sbyte FromLong(long l) => (sbyte)l;
            public uint FromULong(ulong u) => (uint)u;
            public byte? Lifted(int? x) => (byte?)x;
            public byte Unwrapped(int? x) => (byte)x;
            public byte Checked(int x) => checked((byte)x);
            public sbyte CheckedSigned(int x) => checked((sbyte)x);
            public ushort CheckedUShort(int x) => checked((ushort)x);
            public uint CheckedUInt(int x) => checked((uint)x);
            public int CheckedInt(uint x) => checked((int)x);
            public byte CheckedMasked(int x) => checked((byte)(x & 0xFF));
            public byte? CheckedLifted(int? x) => checked((byte?)x);
            public byte CheckedFromDouble(double d) => checked((byte)d);
            public byte CheckedFromLong(long l) => checked((byte)l);
            public int? CheckedLiftedFromLong(long? l) => checked((int?)l);
        }
        """;

    private const string CompoundSource = """
        [Transpile]
        public class Small
        {
            public byte Field;
            public short Property { get; set; }
            public byte[] Cells = new byte[4];
            public sbyte? Optional;

            public byte Add(byte b) { b += 1; return b; }
            public byte Increment(byte b) { b++; return b; }
            public sbyte Decrement(sbyte b) { --b; return b; }
            public byte Postfix(byte b) { var old = b++; return old; }
            public short Multiply(short s) { s *= 3; return s; }
            public sbyte Divide(sbyte s, sbyte d) { s /= d; return s; }
            public byte DivideUnsigned(byte b, byte d) { b /= d; return b; }
            public ushort ShiftLeft(ushort s) { s <<= 4; return s; }
            public ushort ShiftRight(ushort s) { s >>= 4; return s; }
            public short ShiftUnsigned(short s) { s >>>= 4; return s; }
            public byte Or(byte b, byte m) { b |= m; b &= m; b ^= m; b %= m; return b; }
            public void OnField() { Field += 200; Field++; }
            public void OnProperty() { Property -= 1; Property--; }
            public void OnElement(int i) { Cells[i] += 200; Cells[i]++; }
            public void OnElementOnce(int i) { Cells[Next(i)] += 1; }
            public void OnOptional() { Optional += 1; Optional++; }
            public int Loop(byte n) { var total = 0; for (byte i = 0; i < n; i++) total += i; return total; }
            public byte Checked(byte b) { checked { b += 1; b++; b <<= 1; b >>= 1; } return b; }
            public void CheckedField() { checked { Field -= 1; Field--; } }
            public int Untouched(int i) { i++; i += 2; i *= 3; return i; }
            private int Next(int i) => i + 1;
        }
        """;

    private const string UnsignedBitsSource = """
        [Transpile]
        public class Bits
        {
            public uint Flags;

            public uint And(uint a, uint b) => a & b;
            public uint AndMask(uint a) => a & 0xFF;
            public uint AndBytes(uint a, byte b) => a & b;
            public uint Or(uint a, uint b) => a | b;
            public uint OrBytes(byte a, ushort b) => (uint)a | b;
            public uint Xor(uint a, uint b) => a ^ b;
            public uint Not(uint a) => ~a;
            public uint ShiftLeft(uint a, int n) => a << n;
            public uint ShiftRight(uint a, int n) => a >> n;
            public uint ShiftUnsigned(uint a, int n) => a >>> n;
            public uint HighBit() => 1u << 31;
            public uint AllBits() => ~0u;
            public uint Mix(uint a, uint b) => (a ^ (b >> 16)) * 0x85EBCA6Bu;
            public uint? Lifted(uint? a, uint? b) => a | b;
            public void Compound(uint b) { Flags |= b; Flags &= b; Flags ^= b; Flags <<= 1; Flags >>= 1; Flags &= 0xFF; }
            public int IntAnd(int a, int b) => a & b | a ^ b;
            public int IntNot(int a) => ~a;
            public int IntShiftLeft(int a, int n) => a << n;
            public int IntShiftRight(int a, int n) => a >> n;
            public int IntShiftUnsigned(int a, int n) => a >>> n;
            public int IntShiftUnsignedMasked(int a, int n) => (a & 0xFFFF) >>> n;
            public int ByteShift(byte b, int n) => b >> n;
        }
        """;

    private const string CheckedArithmeticSource = """
        [Transpile]
        public class Checks
        {
            public int Total;

            public int Add(int a, int b) => checked(a + b);
            public int Sub(int a, int b) => checked(a - b);
            public int Mul(int a, int b) => checked(a * b);
            public int Neg(int a) => checked(-a);
            public uint UAdd(uint a, uint b) => checked(a + b);
            public uint USub(uint a, uint b) => checked(a - b);
            public uint UMul(uint a, uint b) => checked(a * b);
            public int Bytes(byte a, byte b) => checked(a + b);
            public int Shorts(short a, short b) => checked(a * b);
            public int UShorts(ushort a, ushort b) => checked(a * b);
            public int NegShort(short a) => checked(-a);
            public int Shift(int a) => checked(a << 1);
            public int Divide(int a, int b) => checked(a / b);
            public int? Lifted(int? a, int? b) => checked(a + b);
            public int Step(int a) { checked { a++; ++a; a--; a += 5; a -= 1; a *= 2; } return a; }
            public int Postfix(int a) { checked { var b = a++; return b; } }
            public uint UStep(uint u) { checked { u++; u -= 1; } return u; }
            public void OnField() { checked { Total++; Total += 2; } }
            public int Inner(int a, int b) => checked(a + unchecked(a * b));
            public int Plain(int a, int b) => a + b * a - 1;
        }
        """;

    private const string ProjectCheckedSource = """
        [Transpile]
        public class Project
        {
            public int Add(int a, int b) => a + b;
            public byte Narrow(int x) => (byte)x;
            public byte Step(byte b) { b++; return b; }
            public int Wrapped(int a, int b) => unchecked(a + b);
            public byte Kept(int x) => unchecked((byte)x);
        }
        """;

    [Test]
    public async Task Conversions_KeepTheLowBitsOrCheckTheRange()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(ConversionsSource);

        await TranspileHelper.AssertGolden("narrow-integer-conversions.ts", files["narrowing.ts"]);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A conversion that cannot change the value emits nothing: a widening,
    /// the same type, and an operand whose values the target holds.
    /// </summary>
    [Test]
    public async Task Conversions_EmitNothingWhenTheValueCannotChange()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(ConversionsSource);
        var narrowing = files["narrowing.ts"];

        await Assert.That(BodyOf(narrowing, "widened")).IsEqualTo("return x;");
        await Assert.That(BodyOf(narrowing, "widenedUnsigned")).IsEqualTo("return x;");
        await Assert.That(BodyOf(narrowing, "same")).IsEqualTo("return x;");
        await Assert.That(BodyOf(narrowing, "widenedSigned")).IsEqualTo("return x;");
        await Assert.That(BodyOf(narrowing, "masked")).IsEqualTo("return x & 255;");
        await Assert.That(BodyOf(narrowing, "checkedMasked")).IsEqualTo("return x & 255;");
        await Assert.That(BodyOf(narrowing, "constant")).IsEqualTo("return 4294967295;");
        await Assert.That(BodyOf(narrowing, "constantByte")).IsEqualTo("return 44;");
    }

    [Test]
    public async Task CompoundAssignmentsAndIncrements_StoreTheNarrowedValue()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(CompoundSource);

        await TranspileHelper.AssertGolden("narrow-integer-compound.ts", files["small.ts"]);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task UnsignedBitwiseResults_AreReadBackAsUnsigned()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(UnsignedBitsSource);

        await TranspileHelper.AssertGolden("uint32-bitwise.ts", files["bits.ts"]);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task CheckedArithmetic_ChecksTheResultRange()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            CheckedArithmeticSource
        );

        await TranspileHelper.AssertGolden("int32-checked-arithmetic.ts", files["checks.ts"]);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// The documented gap: <c>int</c> / <c>uint</c> arithmetic in the default
    /// (unchecked) context is not wrapped.
    /// </summary>
    [Test]
    public async Task UncheckedArithmetic_IsNotWrapped()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(CheckedArithmeticSource);

        await Assert.That(BodyOf(files["checks.ts"], "plain")).IsEqualTo("return a + b * a - 1;");
    }

    /// <summary>
    /// A project compiled with <c>CheckForOverflowUnderflow</c> checks every
    /// operation outside an <c>unchecked</c> context.
    /// </summary>
    [Test]
    public async Task ProjectWideOverflowChecks_CheckEveryOperation()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithOverflowChecks(
            ProjectCheckedSource
        );

        await TranspileHelper.AssertGolden("int32-project-checked.ts", files["project.ts"]);
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A Dart <c>int</c> is 64-bit, so a narrowing keeps the low bits with
    /// <c>toSigned</c> / <c>toUnsigned</c>; a checked context wraps the same
    /// way instead of throwing (a documented gap).
    /// </summary>
    [Test]
    public async Task Dart_NarrowsWithToSignedAndToUnsigned()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(
            ConversionsSource + CompoundSource + UnsignedBitsSource + CheckedArithmeticSource
        );

        var narrowing = files["narrowing.dart"];
        var small = files["small.dart"];
        var bits = files["bits.dart"];
        var checks = files["checks.dart"];

        await Assert.That(narrowing).Contains("int intToUInt(int x) => x.toUnsigned(32);");
        await Assert.That(narrowing).Contains("int intToShort(int x) => x.toSigned(16);");
        await Assert.That(narrowing).Contains("int sum(int a, int b) => (a + b).toUnsigned(8);");
        await Assert.That(narrowing).Contains("int checked(int x) => x.toUnsigned(8);");
        await Assert.That(small).Contains("b = (b + 1).toUnsigned(8);");
        await Assert.That(small).Contains("this.field = (this.field + 200).toUnsigned(8);");
        await Assert.That(small).Contains("s = (s * 3).toSigned(16);");
        await Assert.That(bits).Contains("int not(int a) => (~a).toUnsigned(32);");
        await Assert.That(bits).Contains("int shiftLeft(int a, int n) => (a << n).toUnsigned(32);");
        await Assert.That(bits).Contains("int shiftRight(int a, int n) => a >> n;");
        await Assert.That(checks).Contains("int add(int a, int b) => (a + b).toSigned(32);");
        await Assert.That(checks).Contains("int plain(int a, int b) => a + b * a - 1;");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>The single statement of the method <paramref name="name"/> in <paramref name="source"/>.</summary>
    private static string BodyOf(string source, string name)
    {
        var lines = source.Split('\n');
        var header = Array.FindIndex(lines, line => line.TrimStart().StartsWith(name + "("));

        return lines[header + 1].Trim();
    }

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature);
}
