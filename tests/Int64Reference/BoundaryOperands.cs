using System.Numerics;

namespace Int64Reference;

/// <summary>
/// The operands each parameter type is exercised with (spec 012 FR-064): zero,
/// one and minus one, the 32-bit limits, 2^53 ± 1 (the edge of exact
/// doubles), the 64-bit limits and 2^63; a nullable parameter also gets
/// <c>null</c>.
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

    private static readonly object?[] Int32Values = [0, 1, -1, int.MaxValue, int.MinValue];

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
    ];

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
            _ when type == typeof(BigInteger) => BigIntegerValues,
            _ => throw new NotSupportedException($"No boundary operands for '{type}'."),
        };
}
