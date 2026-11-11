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
            public byte FromULongProduct(ulong a, ulong b) => (byte)(int)(a * b);
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
            public int IntShiftLeftConstant(byte b) => b << 8;
            public void IntCompoundShift(int[] cells, int n) { cells[0] <<= n; }
            public uint ShiftLeftNarrow(byte b) => (uint)b << 8;
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

    private const string CompoundShiftSource = """
        using System.Threading.Tasks;

        [Transpile]
        public class Cells
        {
            public int Signed;
            public uint Unsigned;
            public byte Small;
            public int[] Slots = new int[4];
            public int Tracked { get; set; }

            public Cells Next() => this;
            public int NextIndex() => 1;
            public int NextCount(int n) => n;
            public Task<int> CountAsync() => Task.FromResult(1);

            public void Field(Cells h, int n) { h.Next().Signed <<= n; }
            public void Element(Cells h, int k, int n) { h.Next().Slots[k++] <<= n; }
            public void IndexAndCount(Cells h, int n) { h.Slots[h.NextIndex()] <<= h.NextCount(n); }
            public void Property(Cells h, int n) { h.Next().Tracked <<= n; }
            public void ArrayReceiver(Cells[] hs) { hs[0].Slots[1] <<= 2; }
            public async Task Awaited(Cells h) { h.Next().Signed <<= await CountAsync(); }
            public void RightShift(Cells h, int n) { h.Next().Signed >>= n; }
            public void UnsignedRightShift(Cells h, int n) { h.Next().Unsigned >>= n; }
            public void UnsignedField(int n) { Unsigned >>= n; }
            public int Value(Cells h, int n) => h.Next().Signed <<= n;
            public void LogicalShiftKeepsTheBinding(Cells h, int n) { h.Next().Signed >>>= n; }
            public void UnsignedLeftShiftKeepsTheBinding(Cells h, int n) { h.Next().Unsigned <<= n; }
            public void NarrowLeftShiftKeepsTheBinding(Cells h, int n) { h.Next().Small <<= n; }
        }
        """;

    private const string AwaitedStoreSource = """
        using System.Threading.Tasks;

        [Transpile]
        public class Stores
        {
            public int Signed;
            public uint Unsigned;
            public long Wide;

            public Stores Next() => this;
            public Task<int> CountAsync() => Task.FromResult(1);
            public Task<long> WideAsync() => Task.FromResult(1L);

            public async Task ShiftLeft(Stores h) { h.Next().Signed <<= await CountAsync(); }
            public async Task ShiftRight(Stores h) { h.Next().Unsigned >>= await CountAsync(); }
            public async Task Add(Stores h) { h.Next().Wide += await WideAsync(); }
            public void Plain(Stores h, int n) { h.Next().Signed <<= n; }
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
    /// The product of two <c>ulong</c>s has no bound the range reading can
    /// state, so the narrowing that follows it is kept.
    /// </summary>
    [Test]
    public async Task Conversions_KeepTheNarrowingAfterAnUnboundedProduct()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(ConversionsSource);

        await Assert
            .That(BodyOf(files["narrowing.ts"], "fromULongProduct"))
            .IsEqualTo("return Number(BigInt.asIntN(32, BigInt.asUintN(64, a * b))) & 0xFF;");
    }

    /// <summary>
    /// JavaScript's <c>&lt;&lt;</c> computes in 32 signed bits, the wrap C#
    /// applies to an <c>int</c> shift, so the reduction the core builds for
    /// it prints as the bare operator and <c>&lt;&lt;=</c> stays compound.
    /// </summary>
    [Test]
    public async Task IntLeftShift_KeepsTheNativeOperator()
    {
        var (files, _) = TranspileHelper.TranspileWithDiagnostics(UnsignedBitsSource);
        var bits = files["bits.ts"];

        await Assert.That(BodyOf(bits, "intShiftLeft")).IsEqualTo("return a << n;");
        await Assert.That(BodyOf(bits, "intShiftLeftConstant")).IsEqualTo("return b << 8;");
        await Assert.That(BodyOf(bits, "intCompoundShift")).IsEqualTo("cells[0] <<= n;");
        await Assert.That(BodyOf(bits, "shiftLeftNarrow")).IsEqualTo("return b << 8;");
    }

    /// <summary>
    /// <c>&lt;&lt;=</c> on an <c>int</c> and <c>&gt;&gt;=</c> on a
    /// <c>uint</c> stay JavaScript's own compound operators, which evaluate
    /// their target once, so no receiver is bound; a store that needs a
    /// reduction around the read-modify-write keeps the binding.
    /// </summary>
    [Test]
    public async Task CompoundShifts_KeepTheNativeOperatorAndBindNoReceiver()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(CompoundShiftSource);
        var cells = files["cells.ts"];

        await TranspileHelper.AssertGolden("int32-compound-shifts.ts", cells);
        await Assert.That(BodyOf(cells, "field")).IsEqualTo("h.next().signed <<= n;");
        await Assert.That(BodyOf(cells, "element")).IsEqualTo("h.next().slots[k++] <<= n;");

        await Assert
            .That(BodyOf(cells, "indexAndCount"))
            .IsEqualTo("h.slots[h.nextIndex()] <<= h.nextCount(n);");

        await Assert.That(BodyOf(cells, "property")).IsEqualTo("h.next().tracked <<= n;");
        await Assert.That(BodyOf(cells, "arrayReceiver")).IsEqualTo("hs[0].slots[1] <<= 2;");

        await Assert
            .That(BodyOf(cells, "awaited"))
            .IsEqualTo("h.next().signed <<= await this.countAsync();");

        await Assert.That(BodyOf(cells, "rightShift")).IsEqualTo("h.next().signed >>= n;");

        await Assert
            .That(BodyOf(cells, "unsignedRightShift"))
            .IsEqualTo("h.next().unsigned >>>= n;");

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A compound assignment or an increment that needs a reduction stores
    /// through the read-modify-write lowering, which a dictionary entry does
    /// not support (the read of a missing key would not throw): it reports
    /// MS0001 at the expression, as the 64-bit forms do.
    /// </summary>
    [Test]
    [Arguments("counts[key]++", "Dictionary<string, int>")]
    [Arguments("counts[key] += 2", "Dictionary<string, int>")]
    public async Task DictionaryEntryInACheckedProject_ReportsUnsupported(
        string expression,
        string dictionary
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithOverflowChecks(
            DictionaryEntrySource(expression, dictionary)
        );

        await AssertReportsTheDictionaryEntry(diagnostics, expression);
    }

    [Test]
    [Arguments("counts[key]++", "Dictionary<string, byte>")]
    [Arguments("counts[key] += 1", "Dictionary<string, byte>")]
    [Arguments("counts[key] |= 1u << 31", "Dictionary<string, uint>")]
    [Arguments("counts[key] <<= 1", "Dictionary<string, int>")]
    [Arguments("counts[key] >>>= 1", "Dictionary<string, int>")]
    [Arguments("counts[key] >>= 1", "Dictionary<string, uint>")]
    public async Task DictionaryEntryOfAReducedStore_ReportsUnsupported(
        string expression,
        string dictionary
    )
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            DictionaryEntrySource(expression, dictionary)
        );

        await AssertReportsTheDictionaryEntry(diagnostics, expression);
    }

    /// <summary>An <c>int</c> entry in the default context needs no reduction and reports nothing.</summary>
    [Test]
    public async Task DictionaryEntryOfAnUnreducedStore_ReportsNothing()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            DictionaryEntrySource("counts[key] += 2", "Dictionary<string, int>")
        );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A Dart <c>int</c> is 64-bit, so a narrowing conversion keeps the low
    /// bits with <c>toSigned</c> / <c>toUnsigned</c>; a checked one prints
    /// its unchecked form (a checked context is a documented gap in Dart).
    /// </summary>
    [Test]
    public async Task Dart_NarrowsConversionsWithToSignedAndToUnsigned()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(ConversionsSource);
        var narrowing = files["narrowing.dart"];

        await Assert.That(narrowing).Contains("int intToUInt(int x) => x.toUnsigned(32);");
        await Assert.That(narrowing).Contains("int intToShort(int x) => x.toSigned(16);");
        await Assert.That(narrowing).Contains("int sum(int a, int b) => (a + b).toUnsigned(8);");
        await Assert.That(narrowing).Contains("int checked(int x) => x.toUnsigned(8);");
        await Assert.That(narrowing).Contains("int checkedInt(int x) => x.toSigned(32);");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    [Test]
    public async Task Dart_NarrowsTheStoresOfTheTypesNarrowerThanInt()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(CompoundSource);
        var small = files["small.dart"];

        await Assert.That(small).Contains("b = (b + 1).toUnsigned(8);");
        await Assert.That(small).Contains("this.field = (this.field + 200).toUnsigned(8);");
        await Assert.That(small).Contains("s = (s * 3).toSigned(16);");
        await Assert.That(small).Contains("this.field = (this.field - 1).toUnsigned(8);");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// <c>&lt;&lt;</c> and <c>~</c> are wrapped by the C# rule; <c>&gt;&gt;&gt;</c>
    /// on an <c>int</c> shifts its 32 bits, so the operand is read as
    /// unsigned before the shift and the result as signed after it.
    /// </summary>
    [Test]
    public async Task Dart_WrapsShiftsAndComplements()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(UnsignedBitsSource);
        var bits = files["bits.dart"];

        await Assert.That(bits).Contains("int not(int a) => (~a).toUnsigned(32);");
        await Assert.That(bits).Contains("int shiftLeft(int a, int n) => (a << n).toUnsigned(32);");
        await Assert.That(bits).Contains("int shiftRight(int a, int n) => a >> n;");

        await Assert
            .That(bits)
            .Contains("int intShiftLeft(int a, int n) => (a << n).toSigned(32);");

        await Assert.That(bits).Contains("int intShiftLeftConstant(int b) => b << 8;");
        await Assert.That(bits).Contains("cells[0] = (cells[0] << n).toSigned(32);");

        await Assert
            .That(bits)
            .Contains(
                "int intShiftUnsigned(int a, int n) => (a.toUnsigned(32) >>> n).toSigned(32);"
            );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// Checked <c>int</c> / <c>uint</c> arithmetic prints as its unchecked
    /// form, which does not wrap, as checked 64-bit arithmetic does
    /// (spec 012 DECIDED-19): Dart never throws <c>OverflowException</c>.
    /// </summary>
    [Test]
    public async Task Dart_PrintsCheckedArithmeticUnchecked()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(CheckedArithmeticSource);
        var checks = files["checks.dart"];

        await Assert.That(checks).Contains("int add(int a, int b) => a + b;");
        await Assert.That(checks).Contains("int uSub(int a, int b) => a - b;");
        await Assert.That(checks).Contains("int neg(int a) => -a;");
        await Assert.That(checks).Contains("a = a + 1;");
        await Assert.That(checks).Contains("this.total = this.total + 2;");
        await Assert.That(checks).Contains("int plain(int a, int b) => a + b * a - 1;");
        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    /// <summary>
    /// A store bound to a receiver evaluated once whose value awaits: Dart
    /// rejects <c>await</c> in a closure that is not async, so the binding
    /// is an async closure whose call is awaited.
    /// </summary>
    [Test]
    public async Task Dart_AwaitsABoundStoreWhoseValueAwaits()
    {
        var (files, diagnostics) = DartTranspileHelper.Transpile(AwaitedStoreSource);
        var stores = files["stores.dart"];

        await Assert
            .That(stores)
            .Contains(
                "await ((receiver$temp$0) async => receiver$temp$0.signed = "
                    + "(receiver$temp$0.signed << await this.countAsync()).toSigned(32))(h.next());"
            );

        await Assert
            .That(stores)
            .Contains(
                "await ((receiver$temp$0) async => receiver$temp$0.unsigned = "
                    + "receiver$temp$0.unsigned >> await this.countAsync())(h.next());"
            );

        await Assert
            .That(stores)
            .Contains(
                "await ((receiver$temp$0) async => receiver$temp$0.wide = "
                    + "receiver$temp$0.wide + await this.wideAsync())(h.next());"
            );

        await Assert
            .That(stores)
            .Contains(
                "((receiver$temp$0) => receiver$temp$0.signed = "
                    + "(receiver$temp$0.signed << n).toSigned(32))(h.next());"
            );

        await Assert.That(UnsupportedFeatures(diagnostics)).IsEmpty();
    }

    private static string DictionaryEntrySource(string expression, string dictionary) =>
        $$"""
            using System.Collections.Generic;

            [Transpile]
            public class Tally
            {
                public void Count({{dictionary}} counts, string key)
                {
                    {{expression}};
                }
            }
            """;

    private static async Task AssertReportsTheDictionaryEntry(
        IReadOnlyList<MetanoDiagnostic> diagnostics,
        string expression
    )
    {
        var unsupported = UnsupportedFeatures(diagnostics).ToList();

        await Assert.That(unsupported.Count).IsEqualTo(1);

        var diagnostic = unsupported[0];
        var location = diagnostic.Location!;

        await Assert
            .That(diagnostic.Message)
            .IsEqualTo($"'{expression}' on a dictionary entry is not supported.");

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Warning);

        await Assert
            .That(location.SourceTree!.GetText().ToString(location.SourceSpan))
            .IsEqualTo(expression);
    }

    /// <summary>The single statement of the method <paramref name="name"/> in <paramref name="source"/>.</summary>
    private static string BodyOf(string source, string name)
    {
        var lines = source.Split('\n');

        var header = Array.FindIndex(
            lines,
            line => line.Trim().StartsWith(name + "(") || line.Trim().StartsWith($"async {name}(")
        );

        if (header < 0)
            throw new InvalidOperationException($"No method '{name}' in the generated source.");

        return lines[header + 1].Trim();
    }

    private static IEnumerable<MetanoDiagnostic> UnsupportedFeatures(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) => diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature);
}
