using System.Numerics;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The conversion rows of the reference matrix (spec 012 FR-010–FR-019,
/// FR-069, FR-070; contracts/ts-emission.md § Conversions), run on .NET by
/// tests/Int64Reference and reproduced by the transpiled twin.
/// </summary>
[NoContainer]
public static class Conversions
{
    public static long FromInt(int value) => value;

    public static long FromUInt(uint value) => value;

    public static long FromChar(char value) => value;

    public static ulong UnsignedFromInt(int value) => (ulong)value;

    public static ulong UnsignedFromIntChecked(int value) => checked((ulong)value);

    public static int ToInt(long value) => (int)value;

    public static int ToIntChecked(long value) => checked((int)value);

    public static uint ToUInt(long value) => (uint)value;

    public static uint ToUIntChecked(long value) => checked((uint)value);

    public static short ToShort(long value) => (short)value;

    public static short ToShortChecked(long value) => checked((short)value);

    public static ushort ToUShort(long value) => (ushort)value;

    public static sbyte ToSByte(long value) => (sbyte)value;

    public static byte ToByte(long value) => (byte)value;

    public static byte ToByteChecked(long value) => checked((byte)value);

    public static char ToChar(long value) => (char)value;

    public static ulong ToULong(long value) => (ulong)value;

    public static ulong ToULongChecked(long value) => checked((ulong)value);

    public static long FromULong(ulong value) => (long)value;

    public static long FromULongChecked(ulong value) => checked((long)value);

    public static int UnsignedToInt(ulong value) => (int)value;

    public static int UnsignedToIntChecked(ulong value) => checked((int)value);

    public static double ToDouble(long value) => value;

    public static double UnsignedToDouble(ulong value) => value;

    public static float ToSingle(long value) => value;

    public static float UnsignedToSingle(ulong value) => value;

    public static long FromDouble(double value) => (long)value;

    public static long FromDoubleChecked(double value) => checked((long)value);

    public static ulong UnsignedFromDouble(double value) => (ulong)value;

    public static ulong UnsignedFromDoubleChecked(double value) => checked((ulong)value);

    public static long FromSingle(float value) => (long)value;

    public static int IntFromDouble(double value) => (int)value;

    public static int IntFromDoubleChecked(double value) => checked((int)value);

    public static uint UIntFromDouble(double value) => (uint)value;

    public static byte ByteFromDouble(double value) => (byte)value;

    public static byte ByteFromDoubleChecked(double value) => checked((byte)value);

    public static short ShortFromDouble(double value) => (short)value;

    public static char CharFromDouble(double value) => (char)value;

    public static BigInteger ToBigInteger(long value) => value;

    public static long FromBigInteger(BigInteger value) => (long)value;

    public static ulong UnsignedFromBigInteger(BigInteger value) => (ulong)value;

    public static long? LiftedFromInt(int? value) => value;

    public static int? LiftedToInt(long? value) => (int?)value;

    public static bool LessThanInt(long left, int right) => left < right;

    public static bool EqualsInt(long left, int right) => left == right;

    public static bool UnsignedGreaterThanInt(ulong left, int right) => left > (ulong)right;
}
