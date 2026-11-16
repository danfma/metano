using System.Globalization;

namespace BclReference;

/// <summary>
/// The rows for the default text of a <c>bool</c>, <c>double</c> and <c>float</c> (#308): what
/// <c>$"{value}"</c>, <c>"x" + value</c>, <c>value.ToString()</c> and <c>string.Join</c> print
/// in the invariant culture. A <c>float</c> row passes the <c>double</c> the TypeScript holds;
/// the helper reads it as the <c>float</c> C# converts it to.
/// </summary>
internal static class TextRows
{
    private static readonly double[] Doubles =
    [
        0,
        1,
        -1,
        0.1,
        0.5,
        100,
        1234.5678,
        0.1 + 0.2,
        1.0 / 3,
        2.0 / 3,
        10.0 / 3,
        1e-7,
        1.5e-7,
        1e-5,
        1.2345e-5,
        0.0001,
        0.00012345,
        1e14,
        1e15,
        1e16,
        123456789012345.0,
        1234567890123456.0,
        12345678901234567.0,
        123456789012345678.0,
        1e21,
        1.5e21,
        1e22,
        1e100,
        1e-100,
        -1e-7,
        -1e21,
        5e-324,
        1.7976931348623157e308,
        2.2250738585072014e-308,
        123456789.123,
        0.30000000000000004,
        4.35,
        100.0 / 3,
        9007199254740993.0,
        1e300,
        double.NaN,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeZero,
        1234567.0,
        12345678.0,
        123456789.0,
        1234567890.0,
        0.001,
        0.01,
        123.456e5,
    ];

    private static readonly double[] Singles =
    [
        0,
        1,
        -1,
        0.1,
        0.5,
        100,
        1234.5678,
        0.1f + 0.2f,
        1f / 3,
        2f / 3,
        10f / 3,
        1e-7,
        1.5e-7,
        1e-5,
        1.2345e-5,
        0.0001,
        0.00012345,
        1e7,
        1e8,
        1e9,
        1e10,
        1e14,
        1e15,
        1e16,
        16777216,
        16777217,
        123456789,
        123456792,
        1234567890,
        1e21,
        1e30,
        1e38,
        3.4028235e38,
        1e-38,
        1e-44,
        1.4e-45,
        -1e-7,
        double.NaN,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeZero,
        0.3,
        0.1 + 0.2,
        1234567.0,
        12345678.0,
        0.001,
        0.01,
        3.14159265358979,
        2.71828182845904,
        8388608.5,
        33554432,
        1e-3,
        123.456e5,
        7.0 / 3,
    ];

    public static void AddTo(FixtureWriter rows)
    {
        rows.Each("formatBoolean", a => $"{(bool?)a[0]}", [true], [false], [null]);

        rows.Each(
            "formatDouble",
            a => Text((double)a[0]!),
            [.. Doubles.Select(value => new object?[] { value })]
        );

        rows.Each(
            "formatDouble",
            a => $"{(double?)a[0]}",
            [
                [null],
            ]
        );

        rows.Each(
            "formatSingle",
            a => Text((float)(double)a[0]!),
            [.. Singles.Select(value => new object?[] { value })]
        );

        rows.Each(
            "formatSingle",
            a => $"{(float?)a[0]}",
            [
                [null],
            ]
        );
    }

    /// <summary>The three spellings agree: interpolation, <c>ToString()</c> and concatenation.</summary>
    private static string Text(double value)
    {
        var interpolated = $"{value}";

        return interpolated == value.ToString() && interpolated == "" + value
            ? interpolated
            : throw new InvalidOperationException($"{value}: the spellings differ");
    }

    private static string Text(float value)
    {
        var interpolated = $"{value}";

        return interpolated == value.ToString() && interpolated == "" + value
            ? interpolated
            : throw new InvalidOperationException($"{value}: the spellings differ");
    }
}
