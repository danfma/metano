using System;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The <c>Convert</c> rows of the reference matrix (spec 012 FR-039,
/// contracts/runtime-int64.md § Convert): rounding half to even, the range
/// checks with .NET's per-type messages, and the string forms with and
/// without a base.
/// </summary>
[NoContainer]
public static class ConvertCalls
{
    public static long ConvertFromDouble(double value) => Convert.ToInt64(value);

    public static ulong ConvertUnsignedFromDouble(double value) => Convert.ToUInt64(value);

    public static long ConvertFromSingle(float value) => Convert.ToInt64(value);

    public static long ConvertFromString(string text) => Convert.ToInt64(text);

    public static ulong ConvertUnsignedFromString(string text) => Convert.ToUInt64(text);

    public static long ConvertFromStringInBase(string text, int fromBase) =>
        Convert.ToInt64(text, fromBase);

    public static ulong ConvertUnsignedFromStringInBase(string text, int fromBase) =>
        Convert.ToUInt64(text, fromBase);

    public static long ConvertFromBoolean(bool value) => Convert.ToInt64(value);

    public static long ConvertFromChar(char value) => Convert.ToInt64(value);

    public static ulong ConvertUnsignedFromInt(int value) => Convert.ToUInt64(value);

    public static ulong ConvertUnsignedFromLong(long value) => Convert.ToUInt64(value);

    public static long ConvertFromULong(ulong value) => Convert.ToInt64(value);

    public static int ConvertToInt32(long value) => Convert.ToInt32(value);

    public static uint ConvertToUInt32(long value) => Convert.ToUInt32(value);

    public static short ConvertToInt16(long value) => Convert.ToInt16(value);

    public static ushort ConvertToUInt16(ulong value) => Convert.ToUInt16(value);

    public static byte ConvertToByte(long value) => Convert.ToByte(value);

    public static sbyte ConvertToSByte(long value) => Convert.ToSByte(value);

    public static char ConvertToChar(long value) => Convert.ToChar(value);

    public static int ConvertUnsignedToInt32(ulong value) => Convert.ToInt32(value);

    public static double ConvertToDouble(long value) => Convert.ToDouble(value);

    public static float ConvertToSingle(ulong value) => Convert.ToSingle(value);

    public static bool ConvertToBoolean(long value) => Convert.ToBoolean(value);
}

/// <summary>
/// The <c>decimal</c> rows of <see cref="ConvertCalls"/>: a static class
/// rather than <c>[NoContainer]</c> functions, whose signatures map
/// <c>decimal</c> to <c>number</c> today; emitted beside them.
/// </summary>
[EmitInFile("convert-calls")]
public static class DecimalConvertCalls
{
    public static long ConvertFromDecimal(decimal value) => Convert.ToInt64(value);

    public static ulong ConvertUnsignedFromDecimal(decimal value) => Convert.ToUInt64(value);
}
