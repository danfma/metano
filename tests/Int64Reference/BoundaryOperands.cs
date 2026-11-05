using System.Numerics;
using System.Reflection;

namespace Int64Reference;

/// <summary>
/// The operands each parameter type is exercised with (spec 012 FR-064): zero,
/// one and minus one, the 32-bit limits, 2^53 ± 1 (the edge of exact
/// doubles), the 64-bit limits and 2^63; ±7 for truncating division and the
/// remainder's sign; shift counts of 32, 63, 64, 70 and −64 (an <c>int</c>
/// count is masked with 63); a nullable parameter also gets <c>null</c>.
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
        "Q",
        "Z",
        "D1000000000",
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
    /// The operands of <paramref name="parameter"/>: its name picks the
    /// <c>string</c> and base operands (<c>format</c>, <c>text</c>,
    /// <c>fromBase</c>), its type the others.
    /// </summary>
    public static IReadOnlyList<object?> For(ParameterInfo parameter) =>
        parameter.Name switch
        {
            "format" => FormatValues,
            "text" => TextValues,
            "fromBase" => BaseValues,
            _ => For(parameter.ParameterType),
        };

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
