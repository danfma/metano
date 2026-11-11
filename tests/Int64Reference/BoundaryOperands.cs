using System.Numerics;
using System.Reflection;

namespace Int64Reference;

/// <summary>
/// The operands each parameter type is exercised with (spec 012 FR-064): zero,
/// one and minus one, the 32-bit limits, 2^53 ± 1 (the edge of exact
/// doubles), the 64-bit limits and 2^63; ±7 for truncating division and the
/// remainder's sign; shift counts of 32, 63, 64, 70 and −64 (an <c>int</c>
/// count is masked with 63); the limits of the types narrower than
/// <c>int</c> and, for a narrowing conversion's <c>source</c>, one past them
/// (issue #639); a nullable parameter also gets <c>null</c>.
/// </summary>
internal static class BoundaryOperands
{
    private static readonly object?[] Int64Values =
    [
        0L,
        1L,
        -1L,
        (long)int.MaxValue,
        (long)int.MinValue,
        (1L << 53) - 1,
        (1L << 53) + 1,
        long.MaxValue,
        long.MinValue,
        1L << 62,
        7L,
        -7L,
    ];

    private static readonly object?[] UInt64Values =
    [
        0UL,
        1UL,
        (ulong)int.MaxValue,
        (ulong)uint.MaxValue,
        (1UL << 53) + 1,
        (ulong)long.MaxValue,
        1UL << 63,
        ulong.MaxValue,
    ];

    private static readonly object?[] Int32Values =
    [
        0,
        1,
        -1,
        int.MaxValue,
        int.MinValue,
        32,
        63,
        64,
        70,
        -64,
    ];

    private static readonly object?[] UInt32Values =
    [
        0U,
        1U,
        (uint)int.MaxValue + 1,
        uint.MaxValue,
    ];

    private static readonly object?[] SByteValues =
    [
        (sbyte)0,
        (sbyte)1,
        (sbyte)-1,
        sbyte.MaxValue,
        sbyte.MinValue,
    ];

    private static readonly object?[] ByteValues =
    [
        (byte)0,
        (byte)1,
        (byte)127,
        (byte)128,
        byte.MaxValue,
    ];

    private static readonly object?[] Int16Values =
    [
        (short)0,
        (short)1,
        (short)-1,
        (short)127,
        (short)128,
        (short)-128,
        (short)-129,
        (short)255,
        (short)256,
        short.MaxValue,
        short.MinValue,
    ];

    private static readonly object?[] UInt16Values =
    [
        (ushort)0,
        (ushort)1,
        (ushort)127,
        (ushort)128,
        (ushort)255,
        (ushort)256,
        (ushort)32767,
        (ushort)32768,
        ushort.MaxValue,
    ];

    /// <summary>
    /// The <c>int</c> sources of a narrowing conversion: the limits of every
    /// narrower type, one past them, and values with the high bit set.
    /// </summary>
    private static readonly object?[] Int32SourceValues =
    [
        0,
        1,
        -1,
        127,
        128,
        -128,
        -129,
        255,
        256,
        32767,
        32768,
        -32768,
        -32769,
        65535,
        65536,
        int.MaxValue,
        int.MinValue,
        0x12345678,
        unchecked((int)0x89ABCDEF),
    ];

    private static readonly object?[] UInt32SourceValues =
    [
        0U,
        1U,
        127U,
        128U,
        255U,
        256U,
        32767U,
        32768U,
        65535U,
        65536U,
        (uint)int.MaxValue,
        (uint)int.MaxValue + 1,
        uint.MaxValue,
        0x89ABCDEFU,
    ];

    private static readonly object?[] Int64SourceValues =
    [
        0L,
        1L,
        -1L,
        127L,
        128L,
        -129L,
        255L,
        256L,
        32768L,
        -32769L,
        65535L,
        65536L,
        (long)int.MaxValue,
        (long)int.MaxValue + 1,
        (long)int.MinValue,
        (long)int.MinValue - 1,
        (long)uint.MaxValue,
        (long)uint.MaxValue + 1,
        0x1_0000_00FFL,
        long.MaxValue,
        long.MinValue,
    ];

    private static readonly object?[] UInt64SourceValues =
    [
        0UL,
        1UL,
        255UL,
        256UL,
        65535UL,
        65536UL,
        (ulong)int.MaxValue,
        (ulong)int.MaxValue + 1,
        (ulong)uint.MaxValue,
        (ulong)uint.MaxValue + 1,
        1UL << 63,
        ulong.MaxValue,
    ];

    private static readonly object?[] CharValues = ['\0', 'A', '\uFFFF'];

    private static readonly object?[] DoubleValues =
    [
        0.0,
        -0.0,
        1.5,
        -1.5,
        3.7,
        -0.9,
        300.5,
        65535.9,
        2147483647.5,
        -2147483648.5,
        4294967296.0,
        9007199254740993.0,
        9223372036854774784.0,
        9223372036854775808.0,
        -9223372036854775808.0,
        -9223372036854777856.0,
        18446744073709549568.0,
        18446744073709551616.0,
        double.NaN,
        double.PositiveInfinity,
        double.NegativeInfinity,
    ];

    private static readonly object?[] SingleValues =
    [
        0f,
        1.5f,
        -1.5f,
        16777217f,
        9.223372E+18f,
        -9.223372E+18f,
        1.8446743E+19f,
        float.NaN,
        float.PositiveInfinity,
    ];

    private static readonly object?[] DecimalValues =
    [
        0m,
        1.5m,
        -0.9m,
        -1.5m,
        9223372036854775807m,
        9223372036854775807.9m,
        9223372036854775808m,
        -9223372036854775808m,
        -9223372036854775809m,
        18446744073709551615.5m,
        18446744073709551616m,
    ];

    private static readonly object?[] BigIntegerValues =
    [
        BigInteger.Zero,
        BigInteger.MinusOne,
        new BigInteger(int.MinValue),
        new BigInteger(int.MinValue) - 1,
        new BigInteger(long.MaxValue),
        new BigInteger(long.MaxValue) + 1,
        new BigInteger(long.MinValue),
        new BigInteger(long.MinValue) - 1,
        new BigInteger(ulong.MaxValue),
        new BigInteger(ulong.MaxValue) + 1,
        new BigInteger(255),
        new BigInteger(256),
        new BigInteger(-129),
        new BigInteger(65536),
        new BigInteger(int.MaxValue),
        new BigInteger(int.MaxValue) + 1,
        new BigInteger(uint.MaxValue),
        new BigInteger(uint.MaxValue) + 1,
        -(BigInteger.One << 40),
        (BigInteger.One << 60) + (BigInteger.One << 36) + 1,
        BigInteger.Pow(10, 30),
        -(BigInteger.One << 96),
        BigInteger.One << 1024,
    ];

    private static readonly object?[] BooleanValues = [false, true];

    /// <summary>
    /// Format strings: the implemented standard formats with and without a
    /// precision, and invalid ones. The valid .NET formats the runtime rejects
    /// with NotSupportedException (MS0066) diverge by design, so the runtime
    /// tests cover them instead.
    /// </summary>
    private static readonly object?[] FormatValues =
    [
        null,
        "",
        "G",
        "g",
        "G1",
        "G2",
        "G5",
        "g5",
        "G19",
        "G20",
        "R",
        "R5",
        "r3",
        "D",
        "D8",
        "d3",
        "D25",
        "N",
        "N0",
        "n3",
        "F",
        "F0",
        "f3",
        "X",
        "x",
        "X0",
        "x4",
        "X20",
        "E",
        "e2",
        "E0",
        "P",
        "P0",
        "p3",
        "C",
        "C0",
        "c4",
        "Q",
        "Z",
        "D1000000000",
    ];

    /// <summary>
    /// The standard numeric formats of <c>double</c> and the integers held in
    /// a JS <c>number</c> (baseline FR-051): every letter with and without a
    /// precision, both cases, precisions past a double's 17 digits, and
    /// formats .NET rejects for one of the two families (<c>D</c> and
    /// <c>X</c> for <c>double</c>). The custom formats, and the integers'
    /// <c>B</c>, which the runtime rejects with NotSupportedException
    /// (MS0001), diverge by design and are covered by the runtime tests.
    /// </summary>
    private static readonly object?[] NumberFormatValues =
    [
        null,
        "",
        "G",
        "g",
        "G0",
        "G1",
        "G2",
        "G3",
        "G5",
        "G15",
        "G16",
        "G17",
        "g20",
        "G30",
        "R",
        "r",
        "R5",
        "r3",
        "F",
        "F0",
        "F1",
        "F2",
        "f3",
        "F10",
        "F20",
        "F40",
        "N",
        "N0",
        "n1",
        "N3",
        "E",
        "E0",
        "e1",
        "E2",
        "E16",
        "E20",
        "E40",
        "P",
        "P0",
        "p1",
        "P4",
        "C",
        "C0",
        "c3",
        "D",
        "D0",
        "D5",
        "d12",
        "X",
        "x",
        "X8",
        "x2",
        "Z",
        "q3",
        "D1000000000",
    ];

    /// <summary>
    /// The <c>double</c> operands of the formatters (baseline FR-051): zeros,
    /// the midpoints that are exact (<c>0.125</c>, <c>2.5</c>: half to even)
    /// and those that only look like one (<c>2.675</c>, <c>1.005</c>: below
    /// it), values on both sides of every notation switch (<c>1E-05</c>,
    /// <c>0.0001</c>, 15 to 17 digits, <c>1E+21</c>), the extremes, a
    /// subnormal, powers of two whose shortest digits are asymmetric, a
    /// widened <c>float</c>, and the non-finite values.
    /// </summary>
    private static readonly object?[] FormattedDoubleValues =
    [
        0.0,
        -0.0,
        1.0,
        -1.0,
        0.5,
        1.5,
        2.5,
        -0.5,
        -2.5,
        0.125,
        0.375,
        0.625,
        -0.125,
        2.675,
        1.005,
        1.45,
        9.995,
        0.045,
        0.015,
        0.005,
        0.025,
        0.95,
        0.05,
        99.5,
        999.5,
        123.456,
        -123.456,
        0.1,
        0.2,
        0.1 + 0.2,
        1.0 / 3,
        2.0 / 3,
        -0.001,
        1e-7,
        1e-5,
        0.0001,
        0.00012345,
        12345.6789,
        1234567.891,
        100.0,
        1e15,
        1e16,
        1e17,
        123456789012345678.0,
        9007199254740993.0,
        4294967296.0,
        1e21,
        1e22,
        1e23,
        8.41e21,
        1e100,
        1e-100,
        1152921504606846976.0,
        1180591620717411303424.0,
        8.98846567431158e307,
        (double)0.1f,
        double.MaxValue,
        double.MinValue,
        double.Epsilon,
        2.2250738585072014e-308,
        4.35e-310,
        double.NaN,
        double.PositiveInfinity,
        double.NegativeInfinity,
    ];

    /// <summary>
    /// The <c>int</c> operands of the formatters: the boundary operands plus
    /// the values whose rounding to fewer significant digits carries or meets
    /// a midpoint (<c>25</c> → <c>3E+001</c>, half away from zero).
    /// </summary>
    private static readonly object?[] FormattedInt32Values =
    [
        .. Int32Values,
        7,
        25,
        -25,
        125,
        255,
        999,
        12345,
        1250000,
        -1234567,
    ];

    /// <summary>
    /// Text to parse: the grammar of every supported <c>NumberStyles</c> and
    /// of <c>Convert</c>'s bases, white space (U+00A0 is not .NET white
    /// space), trailing NULs, the 64-bit limits and one past them.
    /// </summary>
    private static readonly object?[] TextValues =
    [
        null,
        "",
        " ",
        "12",
        " 12 ",
        "\t12\n",
        "\u00A012",
        "+5",
        "-5",
        "--5",
        "- 5",
        "-5-",
        "5-",
        "5 -",
        "1,234",
        ",1",
        "1,,2",
        "1,",
        "-1,234.000",
        "1.5",
        "1.",
        ".0",
        ".",
        "-0",
        "-0.0",
        "0,-",
        "0x10",
        "0X10",
        "1e3",
        "12\0\0",
        "12 \0",
        "\012",
        "1 2",
        "00000000000000000000000000012",
        "9223372036854775807",
        "9223372036854775808",
        "-9223372036854775808",
        "-9223372036854775809",
        "18446744073709551615",
        "18446744073709551616",
        "FF",
        "ff",
        "FFFFFFFFFFFFFFFF",
        "7FFFFFFFFFFFFFFF",
        "8000000000000000",
        "10000000000000000",
        "0000000000000000000FF",
        " FF ",
        "-FF",
        "101",
        "777",
        "1777777777777777777777",
        "2000000000000000000000",
        "z",
    ];

    /// <summary>The bases of <c>Convert.ToInt64(string, int)</c>, and one it rejects.</summary>
    private static readonly object?[] BaseValues = [2, 8, 10, 16, 3];

    /// <summary>
    /// In-range indices for the array rows: an out-of-range index reads
    /// <c>undefined</c> in the generated arrays (011 OD-4, documented), where
    /// .NET throws, so the fixture keeps to the 3-element arrays below.
    /// </summary>
    private static readonly object?[] IndexValues = [0L, 1L, 2L];

    /// <summary>
    /// Array-creation lengths: sizes whose result text stays readable. A
    /// negative length is left out on purpose: the generated
    /// <c>new Array</c> throws <c>RangeError</c> where .NET throws
    /// <c>OverflowException</c> (012g, a documented divergence).
    /// </summary>
    private static readonly object?[] LengthValues = [0, 1, 3, 10];

    /// <summary>
    /// The arrays the indexing rows read from: three elements so every
    /// <see cref="IndexValues"/> operand is in range.
    /// </summary>
    private static readonly object?[] LongArrayValues =
    [
        new long[] { 5, -6, (1L << 53) + 1 },
        new long[] { 0, 0, long.MinValue },
    ];

    private static readonly object?[] IntArrayValues = [new int[] { 7, -8, int.MaxValue }];

    /// <summary>
    /// The boxed operands of <c>Equals(object)</c>: a <c>long</c> next to an
    /// <c>int</c>, a <c>ulong</c>, a <c>double</c>, a string and
    /// <c>null</c>, so the equality rows pin that a boxed <c>long</c> is
    /// equal only to another boxed <c>long</c> (R2, FR-047).
    /// </summary>
    private static readonly object?[] BoxedValues =
    [
        new BoxedValue("Int64", 5L),
        new BoxedValue("Int64", 7L),
        new BoxedValue("Int32", 5),
        new BoxedValue("Int64", (1L << 53) + 1),
        new BoxedValue("UInt64", ulong.MaxValue),
        new BoxedValue("Double", 5.0),
        new BoxedValue("String", "5"),
        null,
    ];

    /// <summary>A boxed value and the .NET type name it is boxed as.</summary>
    internal readonly record struct BoxedValue(string TypeName, object Value);

    /// <summary>
    /// The operands of <paramref name="parameter"/>: its name picks the
    /// <c>string</c>, base, index, length, array, boxed, narrowing-source,
    /// non-zero and formatter operands (<c>format</c>, <c>numberFormat</c>,
    /// <c>text</c>, <c>fromBase</c>, <c>index</c>, <c>length</c>,
    /// <c>values</c>, <c>boxed</c>, <c>source</c>, <c>divisor</c>,
    /// <c>formatted</c>), its type the others.
    /// </summary>
    public static IReadOnlyList<object?> For(ParameterInfo parameter) =>
        parameter.Name switch
        {
            "format" => FormatValues,
            "numberFormat" => NumberFormatValues,
            "formatted" => FormattedValuesFor(parameter.ParameterType),
            "text" => TextValues,
            "fromBase" => BaseValues,
            "index" => IndexValues,
            "length" => LengthValues,
            "values" => ValuesArrayFor(parameter.ParameterType),
            "boxed" => BoxedValues,
            "source" => SourceValuesFor(parameter.ParameterType),
            "divisor" => [.. For(parameter.ParameterType).Where(IsNotZero)],
            _ => For(parameter.ParameterType),
        };

    /// <summary>
    /// The operands of a narrowing conversion's <c>source</c> parameter: the
    /// narrowing sets of the 32-bit and 64-bit integers, the type's own
    /// operands otherwise; a nullable parameter also gets <c>null</c>.
    /// </summary>
    private static IReadOnlyList<object?> SourceValuesFor(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying
            ? [.. SourceValuesFor(underlying), null]
            : Type.GetTypeCode(type) switch
            {
                TypeCode.Int32 => Int32SourceValues,
                TypeCode.UInt32 => UInt32SourceValues,
                TypeCode.Int64 => Int64SourceValues,
                TypeCode.UInt64 => UInt64SourceValues,
                _ => ValuesOf(type),
            };

    /// <summary>
    /// The operands of a formatter's <c>formatted</c> parameter: the richer
    /// sets of <c>double</c> and <c>int</c>, the type's own operands
    /// otherwise; a nullable parameter also gets <c>null</c>.
    /// </summary>
    private static IReadOnlyList<object?> FormattedValuesFor(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying
            ? [.. FormattedValuesFor(underlying), null]
            : Type.GetTypeCode(type) switch
            {
                TypeCode.Double => FormattedDoubleValues,
                TypeCode.Int32 => FormattedInt32Values,
                _ => ValuesOf(type),
            };

    /// <summary>A <c>divisor</c> operand other than zero, so the rows stay on the stored value.</summary>
    private static bool IsNotZero(object? operand) =>
        operand is null || Convert.ToDecimal(operand) != 0m;

    /// <summary>The operand arrays of an array parameter of element <paramref name="type"/>.</summary>
    private static IReadOnlyList<object?> ValuesArrayFor(Type type) =>
        type.GetElementType() == typeof(int) ? IntArrayValues : LongArrayValues;

    /// <summary>The operands of a parameter of type <paramref name="type"/>.</summary>
    public static IReadOnlyList<object?> For(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying
            ? [.. For(underlying), null]
            : ValuesOf(type);

    private static IReadOnlyList<object?> ValuesOf(Type type) =>
        Type.GetTypeCode(type) switch
        {
            TypeCode.Int64 => Int64Values,
            TypeCode.UInt64 => UInt64Values,
            TypeCode.SByte => SByteValues,
            TypeCode.Byte => ByteValues,
            TypeCode.Int16 => Int16Values,
            TypeCode.UInt16 => UInt16Values,
            TypeCode.Int32 => Int32Values,
            TypeCode.UInt32 => UInt32Values,
            TypeCode.Char => CharValues,
            TypeCode.Double => DoubleValues,
            TypeCode.Single => SingleValues,
            TypeCode.Decimal => DecimalValues,
            TypeCode.Boolean => BooleanValues,
            _ when type == typeof(BigInteger) => BigIntegerValues,
            _ => throw new NotSupportedException($"No boundary operands for '{type}'."),
        };
}
