using System.Numerics;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The <c>BigInteger</c> rows of the reference matrix (spec 012 FR-003):
/// the explicit conversions out of a <c>BigInteger</c> into <c>int</c> and
/// narrower types (always checked, with .NET's split between the generic
/// and the per-type messages), into <c>double</c> / <c>float</c> (.NET's
/// truncated significand), and from <c>double</c>.
/// </summary>
[NoContainer]
public static class BigIntegerConversions
{
    public static int BigToInt(BigInteger value) => (int)value;

    public static uint BigToUInt(BigInteger value) => (uint)value;

    public static short BigToShort(BigInteger value) => (short)value;

    public static ushort BigToUShort(BigInteger value) => (ushort)value;

    public static sbyte BigToSByte(BigInteger value) => (sbyte)value;

    public static byte BigToByte(BigInteger value) => unchecked((byte)value);

    public static char BigToChar(BigInteger value) => (char)value;

    public static double BigToDouble(BigInteger value) => (double)value;

    public static float BigToSingle(BigInteger value) => (float)value;

    public static BigInteger BigFromDouble(double value) => (BigInteger)value;

    public static BigInteger BigFromSingle(float value) => (BigInteger)value;
}

/// <summary>
/// The <c>decimal</c> rows of <see cref="BigIntegerConversions"/>: a static
/// class rather than <c>[NoContainer]</c> functions, whose signatures map
/// <c>decimal</c> to <c>number</c> today; emitted beside them.
/// </summary>
[EmitInFile("big-integer-conversions")]
public static class DecimalBigIntegerConversions
{
    public static decimal BigToDecimal(BigInteger value) => (decimal)value;

    public static BigInteger BigFromDecimal(decimal value) => (BigInteger)value;
}
