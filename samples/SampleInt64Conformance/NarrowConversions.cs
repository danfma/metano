using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The conversion rows of the integers of 32 bits or fewer (spec 012
/// FR-074, FR-077, issue #639): every pair of source and target among
/// <c>sbyte</c>, <c>byte</c>, <c>short</c>, <c>ushort</c>, <c>int</c> and
/// <c>uint</c>, plus <c>long</c>, <c>ulong</c> and <c>double</c> sources,
/// unchecked and checked. A parameter named <c>source</c> gets the narrowing
/// boundary operands of tests/Int64Reference (the limits of every width,
/// one past them, and values with the high bit set).
/// </summary>
[NoContainer]
public static class NarrowConversions
{
    public static sbyte SByteToSByte(sbyte source) => (sbyte)source;

    public static sbyte SByteToSByteChecked(sbyte source) => checked((sbyte)source);

    public static byte SByteToByte(sbyte source) => (byte)source;

    public static byte SByteToByteChecked(sbyte source) => checked((byte)source);

    public static short SByteToShort(sbyte source) => (short)source;

    public static short SByteToShortChecked(sbyte source) => checked((short)source);

    public static ushort SByteToUShort(sbyte source) => (ushort)source;

    public static ushort SByteToUShortChecked(sbyte source) => checked((ushort)source);

    public static int SByteToInt(sbyte source) => (int)source;

    public static int SByteToIntChecked(sbyte source) => checked((int)source);

    public static uint SByteToUInt(sbyte source) => (uint)source;

    public static uint SByteToUIntChecked(sbyte source) => checked((uint)source);

    public static sbyte ByteToSByte(byte source) => (sbyte)source;

    public static sbyte ByteToSByteChecked(byte source) => checked((sbyte)source);

    public static byte ByteToByte(byte source) => (byte)source;

    public static byte ByteToByteChecked(byte source) => checked((byte)source);

    public static short ByteToShort(byte source) => (short)source;

    public static short ByteToShortChecked(byte source) => checked((short)source);

    public static ushort ByteToUShort(byte source) => (ushort)source;

    public static ushort ByteToUShortChecked(byte source) => checked((ushort)source);

    public static int ByteToInt(byte source) => (int)source;

    public static int ByteToIntChecked(byte source) => checked((int)source);

    public static uint ByteToUInt(byte source) => (uint)source;

    public static uint ByteToUIntChecked(byte source) => checked((uint)source);

    public static sbyte ShortToSByte(short source) => (sbyte)source;

    public static sbyte ShortToSByteChecked(short source) => checked((sbyte)source);

    public static byte ShortToByte(short source) => (byte)source;

    public static byte ShortToByteChecked(short source) => checked((byte)source);

    public static short ShortToShort(short source) => (short)source;

    public static short ShortToShortChecked(short source) => checked((short)source);

    public static ushort ShortToUShort(short source) => (ushort)source;

    public static ushort ShortToUShortChecked(short source) => checked((ushort)source);

    public static int ShortToInt(short source) => (int)source;

    public static int ShortToIntChecked(short source) => checked((int)source);

    public static uint ShortToUInt(short source) => (uint)source;

    public static uint ShortToUIntChecked(short source) => checked((uint)source);

    public static sbyte UShortToSByte(ushort source) => (sbyte)source;

    public static sbyte UShortToSByteChecked(ushort source) => checked((sbyte)source);

    public static byte UShortToByte(ushort source) => (byte)source;

    public static byte UShortToByteChecked(ushort source) => checked((byte)source);

    public static short UShortToShort(ushort source) => (short)source;

    public static short UShortToShortChecked(ushort source) => checked((short)source);

    public static ushort UShortToUShort(ushort source) => (ushort)source;

    public static ushort UShortToUShortChecked(ushort source) => checked((ushort)source);

    public static int UShortToInt(ushort source) => (int)source;

    public static int UShortToIntChecked(ushort source) => checked((int)source);

    public static uint UShortToUInt(ushort source) => (uint)source;

    public static uint UShortToUIntChecked(ushort source) => checked((uint)source);

    public static sbyte IntToSByte(int source) => (sbyte)source;

    public static sbyte IntToSByteChecked(int source) => checked((sbyte)source);

    public static byte IntToByte(int source) => (byte)source;

    public static byte IntToByteChecked(int source) => checked((byte)source);

    public static short IntToShort(int source) => (short)source;

    public static short IntToShortChecked(int source) => checked((short)source);

    public static ushort IntToUShort(int source) => (ushort)source;

    public static ushort IntToUShortChecked(int source) => checked((ushort)source);

    public static int IntToInt(int source) => (int)source;

    public static int IntToIntChecked(int source) => checked((int)source);

    public static uint IntToUInt(int source) => (uint)source;

    public static uint IntToUIntChecked(int source) => checked((uint)source);

    public static sbyte UIntToSByte(uint source) => (sbyte)source;

    public static sbyte UIntToSByteChecked(uint source) => checked((sbyte)source);

    public static byte UIntToByte(uint source) => (byte)source;

    public static byte UIntToByteChecked(uint source) => checked((byte)source);

    public static short UIntToShort(uint source) => (short)source;

    public static short UIntToShortChecked(uint source) => checked((short)source);

    public static ushort UIntToUShort(uint source) => (ushort)source;

    public static ushort UIntToUShortChecked(uint source) => checked((ushort)source);

    public static int UIntToInt(uint source) => (int)source;

    public static int UIntToIntChecked(uint source) => checked((int)source);

    public static uint UIntToUInt(uint source) => (uint)source;

    public static uint UIntToUIntChecked(uint source) => checked((uint)source);

    public static sbyte LongToSByte(long source) => (sbyte)source;

    public static sbyte LongToSByteChecked(long source) => checked((sbyte)source);

    public static byte LongToByte(long source) => (byte)source;

    public static byte LongToByteChecked(long source) => checked((byte)source);

    public static short LongToShort(long source) => (short)source;

    public static short LongToShortChecked(long source) => checked((short)source);

    public static ushort LongToUShort(long source) => (ushort)source;

    public static ushort LongToUShortChecked(long source) => checked((ushort)source);

    public static int LongToInt(long source) => (int)source;

    public static int LongToIntChecked(long source) => checked((int)source);

    public static uint LongToUInt(long source) => (uint)source;

    public static uint LongToUIntChecked(long source) => checked((uint)source);

    public static sbyte ULongToSByte(ulong source) => (sbyte)source;

    public static sbyte ULongToSByteChecked(ulong source) => checked((sbyte)source);

    public static byte ULongToByte(ulong source) => (byte)source;

    public static byte ULongToByteChecked(ulong source) => checked((byte)source);

    public static short ULongToShort(ulong source) => (short)source;

    public static short ULongToShortChecked(ulong source) => checked((short)source);

    public static ushort ULongToUShort(ulong source) => (ushort)source;

    public static ushort ULongToUShortChecked(ulong source) => checked((ushort)source);

    public static int ULongToInt(ulong source) => (int)source;

    public static int ULongToIntChecked(ulong source) => checked((int)source);

    public static uint ULongToUInt(ulong source) => (uint)source;

    public static uint ULongToUIntChecked(ulong source) => checked((uint)source);

    public static sbyte DoubleToSByte(double source) => (sbyte)source;

    public static sbyte DoubleToSByteChecked(double source) => checked((sbyte)source);

    public static byte DoubleToByte(double source) => (byte)source;

    public static byte DoubleToByteChecked(double source) => checked((byte)source);

    public static short DoubleToShort(double source) => (short)source;

    public static short DoubleToShortChecked(double source) => checked((short)source);

    public static ushort DoubleToUShort(double source) => (ushort)source;

    public static ushort DoubleToUShortChecked(double source) => checked((ushort)source);

    public static int DoubleToInt(double source) => (int)source;

    public static int DoubleToIntChecked(double source) => checked((int)source);

    public static uint DoubleToUInt(double source) => (uint)source;

    public static uint DoubleToUIntChecked(double source) => checked((uint)source);

    public static byte? LiftedIntToByte(int? source) => (byte?)source;

    public static byte? LiftedIntToByteChecked(int? source) => checked((byte?)source);

    public static uint? LiftedIntToUInt(int? source) => (uint?)source;

    public static short? LiftedUIntToShortChecked(uint? source) => checked((short?)source);

    public static int? LiftedLongToIntChecked(long? source) => checked((int?)source);

    public static byte SumToByte(byte a, byte b) => (byte)(a + b);

    public static sbyte ProductToSByte(sbyte a, sbyte b) => (sbyte)(a * b);

    public static short NegateToShort(short a) => (short)-a;

    public static ushort ComplementToUShort(ushort a) => (ushort)~a;

    public static byte MaskToByte(int source) => (byte)(source & 0xFF);

    public static byte HighByte(int source) => (byte)(source >> 24);

    public static uint ConstantUInt() => unchecked((uint)-1);

    public static byte ConstantByte() => unchecked((byte)300);

    public static sbyte ConstantSByte() => unchecked((sbyte)0x80);
}
