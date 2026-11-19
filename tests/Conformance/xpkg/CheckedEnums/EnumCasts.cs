using Metano.Annotations;

[assembly: TranspileAssembly]
[assembly: EmitPackage("conformance-checked-enums", Version = "workspace:*")]

namespace CheckedEnums;

public enum ByteEnum : byte
{
    One = 1,
}

public enum SByteEnum : sbyte
{
    One = 1,
}

public enum ShortEnum : short
{
    One = 1,
}

public enum UShortEnum : ushort
{
    One = 1,
}

public enum IntEnum : int
{
    One = 1,
}

public enum UIntEnum : uint
{
    One = 1,
}

/// <summary>Project-wide checked enum conversions, without a checked syntax wrapper (#708).</summary>
public static class EnumCasts
{
    public static ByteEnum Byte(int value) => (ByteEnum)value;

    public static SByteEnum SByte(int value) => (SByteEnum)value;

    public static ShortEnum Short(int value) => (ShortEnum)value;

    public static UShortEnum UShort(int value) => (UShortEnum)value;

    public static IntEnum Int(uint value) => (IntEnum)value;

    public static UIntEnum UInt(int value) => (UIntEnum)value;

    public static ByteEnum InnerUnchecked(int value) => unchecked((ByteEnum)value);

    public static ByteEnum Add(ByteEnum value, byte step) => value + step;

    public static ByteEnum AddUnchecked(ByteEnum value, byte step) => unchecked(value + step);

    public static ByteEnum Increment(ByteEnum value) => ++value;

    public static ByteEnum Compound(ByteEnum value, byte step)
    {
        value += step;
        return value;
    }
}
