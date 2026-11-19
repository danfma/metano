namespace Conformance;

public enum C8Byte : byte
{
    One = 1,
}

public enum C8SByte : sbyte
{
    One = 1,
}

public enum C8Short : short
{
    One = 1,
}

public enum C8UShort : ushort
{
    One = 1,
}

public enum C8Int : int
{
    One = 1,
}

public enum C8UInt : uint
{
    One = 1,
}

[System.Flags]
public enum C8Flags : byte
{
    One = 1,
}

/// <summary>Spec 011 FR-044: enum casts use the underlying integer conversion (#708).</summary>
public static class EnumCastProbes
{
    private static C8Byte Byte(int value) => unchecked((C8Byte)value);

    private static C8SByte SByte(int value) => unchecked((C8SByte)value);

    private static C8Short Short(int value) => unchecked((C8Short)value);

    private static C8UShort UShort(int value) => unchecked((C8UShort)value);

    private static C8Int Int(uint value) => unchecked((C8Int)value);

    private static C8UInt UInt(int value) => unchecked((C8UInt)value);

    private static C8Byte CheckByte(long value) => checked((C8Byte)value);

    private static C8SByte CheckSByte(long value) => checked((C8SByte)value);

    private static C8Short CheckShort(long value) => checked((C8Short)value);

    private static C8UShort CheckUShort(long value) => checked((C8UShort)value);

    private static C8Int CheckInt(long value) => checked((C8Int)value);

    private static C8UInt CheckUInt(long value) => checked((C8UInt)value);

    public static string UncheckedByte() => ((byte)Byte(-1)).ToString();

    public static string UncheckedSByte() => ((sbyte)SByte(128)).ToString();

    public static string UncheckedShort() => ((short)Short(32768)).ToString();

    public static string UncheckedUShort() => ((ushort)UShort(-1)).ToString();

    public static string UncheckedInt() => ((int)Int(2147483648u)).ToString();

    public static string UncheckedUInt() => ((uint)UInt(-1)).ToString();

    public static string ConstantByte() => ((byte)unchecked((C8Byte)(-1))).ToString();

    public static string ConstantUInt() => ((uint)unchecked((C8UInt)(-1))).ToString();

    private static byte ExpressionByte(int value) => (byte)checked((C8Byte)value);

    private static sbyte ExpressionSByte(int value) => (sbyte)checked((C8SByte)value);

    private static short ExpressionShort(int value) => (short)checked((C8Short)value);

    private static ushort ExpressionUShort(int value) => (ushort)checked((C8UShort)value);

    private static int ExpressionInt(uint value) => (int)checked((C8Int)value);

    private static uint ExpressionUInt(int value) => (uint)checked((C8UInt)value);

    public static string CheckedByte() => ExpressionByte(-1).ToString();

    public static string CheckedSByte() => ExpressionSByte(128).ToString();

    public static string CheckedShort() => ExpressionShort(32768).ToString();

    public static string CheckedUShort() => ExpressionUShort(-1).ToString();

    public static string CheckedInt() => ExpressionInt(2147483648u).ToString();

    public static string CheckedUInt() => ExpressionUInt(-1).ToString();

    private static byte BlockByte(int value)
    {
        checked
        {
            return (byte)(C8Byte)value;
        }
    }

    private static sbyte BlockSByte(int value)
    {
        checked
        {
            return (sbyte)(C8SByte)value;
        }
    }

    private static short BlockShort(int value)
    {
        checked
        {
            return (short)(C8Short)value;
        }
    }

    private static ushort BlockUShort(int value)
    {
        checked
        {
            return (ushort)(C8UShort)value;
        }
    }

    private static int BlockInt(uint value)
    {
        checked
        {
            return (int)(C8Int)value;
        }
    }

    private static uint BlockUInt(int value)
    {
        checked
        {
            return (uint)(C8UInt)value;
        }
    }

    public static string CheckedBlockByte() => BlockByte(-1).ToString();

    public static string CheckedBlockSByte() => BlockSByte(128).ToString();

    public static string CheckedBlockShort() => BlockShort(32768).ToString();

    public static string CheckedBlockUShort() => BlockUShort(-1).ToString();

    public static string CheckedBlockInt() => BlockInt(2147483648u).ToString();

    public static string CheckedBlockUInt() => BlockUInt(-1).ToString();

    public static string ProjectCheckedByte() => ((byte)CheckedEnums.EnumCasts.Byte(-1)).ToString();

    public static string ProjectCheckedSByte() =>
        ((sbyte)CheckedEnums.EnumCasts.SByte(128)).ToString();

    public static string ProjectCheckedShort() =>
        ((short)CheckedEnums.EnumCasts.Short(32768)).ToString();

    public static string ProjectCheckedUShort() =>
        ((ushort)CheckedEnums.EnumCasts.UShort(-1)).ToString();

    public static string ProjectCheckedInt() =>
        ((int)CheckedEnums.EnumCasts.Int(2147483648u)).ToString();

    public static string ProjectCheckedUInt() => ((uint)CheckedEnums.EnumCasts.UInt(-1)).ToString();

    public static string ProjectInnerUnchecked() =>
        ((byte)CheckedEnums.EnumCasts.InnerUnchecked(-1)).ToString();

    public static string BoundaryByte() => ExpressionByte(0) + "," + ExpressionByte(255);

    public static string BoundarySByte() => ExpressionSByte(-128) + "," + ExpressionSByte(127);

    public static string BoundaryShort() => ExpressionShort(-32768) + "," + ExpressionShort(32767);

    public static string BoundaryUShort() => ExpressionUShort(0) + "," + ExpressionUShort(65535);

    public static string BoundaryInt() =>
        (int)CheckInt(-2147483648L) + "," + (int)CheckInt(2147483647L);

    public static string BoundaryUInt() => (uint)CheckUInt(0) + "," + (uint)CheckUInt(4294967295L);

    private static byte InnerUnchecked(int value) => (byte)checked(unchecked((C8Byte)value));

    public static string NestedUnchecked() => InnerUnchecked(-1).ToString();

    private static C8Byte FromEnum(C8Short value) => unchecked((C8Byte)value);

    private static C8Byte CheckedFromEnum(C8Short value) => checked((C8Byte)value);

    private static C8Flags Flags(int value) => unchecked((C8Flags)value);

    private static C8Flags CheckedFlags(int value) => checked((C8Flags)value);

    private static C8SByte SignedEnum(C8Byte value) => unchecked((C8SByte)value);

    public static string EnumToEnum() => ((byte)FromEnum(Short(-1))).ToString();

    public static string EnumSignedness() => ((sbyte)SignedEnum(Byte(255))).ToString();

    public static string CheckedEnumToEnum() => ((byte)CheckedFromEnum(Short(-1))).ToString();

    public static string UncheckedFlags() => ((byte)Flags(-1)).ToString();

    public static string CheckedFlagsOverflow() => ((byte)CheckedFlags(-1)).ToString();

    private static C8Byte? Nullable(int? value) => unchecked((C8Byte?)value);

    private static C8Byte? CheckedNullable(int? value) => checked((C8Byte?)value);

    public static string NullableNull() => (Nullable(null) is null).ToString();

    public static string NullableInRange() => ((byte)Nullable(42)!.Value).ToString();

    public static string NullableWrap() => ((byte)Nullable(-1)!.Value).ToString();

    public static string CheckedNullableNull() => (CheckedNullable(null) is null).ToString();

    public static string CheckedNullableInRange() => ((byte)CheckedNullable(255)!.Value).ToString();

    public static string CheckedNullableOverflow() => ((byte)CheckedNullable(-1)!.Value).ToString();

    private static int? Next(int? value, int[] calls)
    {
        calls[0]++;
        return value;
    }

    private static string Once(int? value)
    {
        var calls = new int[1];
        var result = unchecked((C8Byte?)Next(value, calls));
        return (result is null ? "null" : ((byte)result.Value).ToString()) + "," + calls[0];
    }

    public static string NullableEvaluatedOnce() => Once(-1);

    public static string NullableNullEvaluatedOnce() => Once(null);

    private static string CheckedOnce(int? value)
    {
        var calls = new int[1];

        try
        {
            var result = checked((C8Byte?)Next(value, calls));
            return (result is null ? "null" : ((byte)result.Value).ToString()) + "," + calls[0];
        }
        catch (OverflowException exception)
        {
            return ProbeUtil.Kind(exception) + ": " + exception.Message + "," + calls[0];
        }
    }

    public static string CheckedNullableEvaluatedOnce() => CheckedOnce(42);

    public static string CheckedNullableOverflowEvaluatedOnce() => CheckedOnce(-1);

    public static string CheckedNullableNullEvaluatedOnce() => CheckedOnce(null);

    private static byte Double(double value) => (byte)unchecked((C8Byte)value);

    private static byte CheckedDouble(double value) => (byte)checked((C8Byte)value);

    private static byte Long(long value) => (byte)unchecked((C8Byte)value);

    private static uint ULong(ulong value) => (uint)unchecked((C8UInt)value);

    private static byte WideEnum(E6Signed value) => (byte)unchecked((C8Byte)value);

    public static string FromDouble() => Double(258.9).ToString();

    public static string CheckedFromDouble() => CheckedDouble(256.0).ToString();

    public static string FromLong() => Long(4294967551L).ToString();

    public static string CheckedFromLong() => ((byte)CheckByte(256L)).ToString();

    public static string FromULong() => ULong(ulong.MaxValue).ToString();

    public static string FromWideEnum() => WideEnum(E6Signed.Minus).ToString();
}
