using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>Spec 011 FR-044 (#708): reuse integer narrowing before asserting the enum.</summary>
public class NarrowEnumCastTests
{
    private const string Source = """
        #nullable enable
        [Transpile, EmitInFile("numeric-enum-casts")]
        public enum ByteEnum : byte { One = 1 }
        [Transpile, EmitInFile("numeric-enum-casts")]
        public enum SByteEnum : sbyte { One = 1 }
        [Transpile, EmitInFile("numeric-enum-casts")]
        public enum ShortEnum : short { One = 1 }
        [Transpile, EmitInFile("numeric-enum-casts")]
        public enum UShortEnum : ushort { One = 1 }
        [Transpile, EmitInFile("numeric-enum-casts")]
        public enum IntEnum : int { One = 1 }
        [Transpile, EmitInFile("numeric-enum-casts")]
        public enum UIntEnum : uint { One = 1 }
        [Transpile, EmitInFile("numeric-enum-casts"), System.Flags]
        public enum FlagsEnum : byte { One = 1 }
        [Transpile, EmitInFile("numeric-enum-casts")]
        public static class EnumCasts
        {
            public static ByteEnum Byte(int x) => unchecked((ByteEnum)x);
            public static SByteEnum SByte(int x) => unchecked((SByteEnum)x);
            public static ShortEnum Short(int x) => unchecked((ShortEnum)x);
            public static UShortEnum UShort(int x) => unchecked((UShortEnum)x);
            public static IntEnum Int(uint x) => unchecked((IntEnum)x);
            public static UIntEnum UInt(int x) => unchecked((UIntEnum)x);
            public static ByteEnum CheckedByte(int x) => checked((ByteEnum)x);
            public static SByteEnum CheckedSByte(int x) => checked((SByteEnum)x);
            public static ShortEnum CheckedShort(int x) => checked((ShortEnum)x);
            public static UShortEnum CheckedUShort(int x) => checked((UShortEnum)x);
            public static IntEnum CheckedInt(uint x) => checked((IntEnum)x);
            public static UIntEnum CheckedUInt(int x) => checked((UIntEnum)x);
            public static ByteEnum Block(int x) { checked { return (ByteEnum)x; } }
            public static ByteEnum Inner(int x) => checked(unchecked((ByteEnum)x));
            public static ByteEnum Constant() => unchecked((ByteEnum)(-1));
            public static UIntEnum ConstantUInt() => unchecked((UIntEnum)(-1));
            public static ByteEnum? Lifted(int? x) => unchecked((ByteEnum?)x);
            public static ByteEnum? CheckedLifted(int? x) => checked((ByteEnum?)x);
            public static ByteEnum? Once(int? x) => unchecked((ByteEnum?)Next(x));
            public static int? Next(int? x) => x;
            public static ByteEnum FromEnum(ShortEnum x) => unchecked((ByteEnum)x);
            public static ByteEnum CheckedFromEnum(ShortEnum x) => checked((ByteEnum)x);
            public static FlagsEnum Flags(int x) => unchecked((FlagsEnum)x);
            public static FlagsEnum CheckedFlags(int x) => checked((FlagsEnum)x);
            public static ByteEnum Double(double x) => unchecked((ByteEnum)x);
            public static ByteEnum CheckedDouble(double x) => checked((ByteEnum)x);
            public static ByteEnum Long(long x) => unchecked((ByteEnum)x);
            public static ByteEnum CheckedLong(long x) => checked((ByteEnum)x);
            public static ByteEnum Masked(int x) => checked((ByteEnum)(x & 255));
            public static IntEnum Widened(byte x) => checked((IntEnum)x);
            public static byte OutOfEnum(ShortEnum x) => unchecked((byte)x);
            public static byte CheckedOutOfEnum(ShortEnum x) => checked((byte)x);
        }
        """;

    [Test]
    public async Task Casts_ReuseIntegerConversionsAndRetainEnumAssertions()
    {
        var (files, diagnostics) = TranspileHelper.TranspileWithDiagnostics(Source);

        await TranspileHelper.AssertGolden("numeric-enum-casts.ts", files["numeric-enum-casts.ts"]);

        await Assert
            .That(diagnostics.Where(d => d.Code == DiagnosticCodes.UnsupportedFeature))
            .IsEmpty();
    }

    [Test]
    [Arguments("Byte", "byte", "(x & 0xFF)")]
    [Arguments("SByte", "sByte", "(x << 24) >> 24")]
    [Arguments("Short", "short", "(x << 16) >> 16")]
    [Arguments("UShort", "uShort", "(x & 0xFFFF)")]
    [Arguments("Int", "int", "(x | 0)")]
    [Arguments("UInt", "uInt", "x >>> 0")]
    public async Task UncheckedCast_UsesUnderlyingWrap(string enumName, string method, string wrap)
    {
        var files = TranspileHelper.Transpile(Source);

        await Assert
            .That(files["numeric-enum-casts.ts"])
            .Contains($"return {wrap} as {enumName}Enum;");

        await Assert.That(files["numeric-enum-casts.ts"]).Contains($"static {method}(x: number)");
    }

    [Test]
    [Arguments("Byte", "checkedNarrowNumber(x, 8, false)")]
    [Arguments("SByte", "checkedNarrowNumber(x, 8, true)")]
    [Arguments("Short", "checkedNarrowNumber(x, 16, true)")]
    [Arguments("UShort", "checkedNarrowNumber(x, 16, false)")]
    [Arguments("Int", "checkedInt32(x)")]
    [Arguments("UInt", "checkedUInt32(x)")]
    public async Task CheckedCast_UsesUnderlyingRangeCheck(string enumName, string check)
    {
        var files = TranspileHelper.Transpile(Source);

        await Assert
            .That(files["numeric-enum-casts.ts"])
            .Contains($"return {check} as {enumName}Enum;");
    }

    [Test]
    public async Task ProjectCheckedCasts_UseTheSyntaxFallbackForEnumerationConversions()
    {
        var (files, _) = TranspileHelper.TranspileWithOverflowChecks(
            """
            [Transpile] public enum ByteEnum : byte { One = 1 }
            [Transpile] public static class ProjectCasts
            {
                public static ByteEnum Cast(int x) => (ByteEnum)x;
                public static ByteEnum Inner(int x) => unchecked((ByteEnum)x);
            }
            """
        );

        await Assert
            .That(files["project-casts.ts"])
            .Contains("return checkedNarrowNumber(x, 8, false) as ByteEnum;");

        await Assert.That(files["project-casts.ts"]).Contains("return (x & 0xFF) as ByteEnum;");
    }

    [Test]
    public async Task ConstantsAndRangeProofs_PreserveTheEnumAssertion()
    {
        var files = TranspileHelper.Transpile(Source);
        var output = files["numeric-enum-casts.ts"];

        await Assert.That(output).Contains("return 255 as ByteEnum;");
        await Assert.That(output).Contains("return 4294967295 as UIntEnum;");
        await Assert.That(output).Contains("return (x & 255) as ByteEnum;");
        await Assert.That(output).Contains("return x as IntEnum;");
    }
}
