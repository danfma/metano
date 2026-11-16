namespace BclReference;

/// <summary>
/// The rows for <c>metano-runtime/system/decimal</c> (#465): the arithmetic the configured
/// decimal.js class shares with <c>System.Decimal</c> (29 significant digits, midpoints to
/// even). Division and results below 1e-28 are not rows: a quotient keeps 29 significant digits
/// where .NET keeps 28 decimal places, and .NET rounds a product like 1e-16 * 1e-16 to zero
/// where decimal.js keeps it, which the module documents.
/// </summary>
internal static class DecimalRows
{
    private const string Max = "79228162514264337593543950335";

    public static void AddTo(FixtureWriter rows)
    {
        rows.Each(
            "decimalPlus",
            a => Text(Parse(a, 0) + Parse(a, 1)),
            ["0.1", "0.2"],
            ["1.5", "2.25"],
            [Max, "-1"],
            ["79228162514264337593543950334", "1"],
            ["-79228162514264337593543950334", "-1"],
            ["1234567890123456789012345678", "0.5"],
            ["1.1111111111111111111111111111", "2.2222222222222222222222222222"],
            ["0.0000000000000000000000000001", "0.0000000000000000000000000002"]
        );

        rows.Each(
            "decimalMinus",
            a => Text(Parse(a, 0) - Parse(a, 1)),
            [Max, "1"],
            [Max, "0"],
            ["0.3", "0.1"],
            ["1", "0.9999999999999999999999999999"],
            ["-5", "5.5"]
        );

        rows.Each(
            "decimalTimes",
            a => Text(Parse(a, 0) * Parse(a, 1)),
            ["1.5", "2"],
            ["0.1", "0.1"],
            ["1.1111111111111111111111111111", "1.1111111111111111111111111111"],
            ["79228162514264337593543950335", "1"],
            ["12345678901234567890", "1.5"],
            ["-0.5", "0.5"]
        );

        rows.Each(
            "decimalDivideExact",
            a => Text(Parse(a, 0) / Parse(a, 1)),
            ["1", "4"],
            ["7", "2"],
            ["1", "8"],
            ["10", "5"],
            ["-9", "3"],
            [Max, "1"]
        );

        rows.Each(
            "decimalRoundToEven",
            a => Text(decimal.Round(Parse(a, 0), (int)a[1]!)),
            ["2.5", 0],
            ["3.5", 0],
            ["-2.5", 0],
            ["0.125", 2],
            ["0.135", 2],
            ["1.005", 2],
            ["79228162514264337593543950335", 0]
        );
    }

    private static decimal Parse(object?[] arguments, int index) =>
        decimal.Parse((string)arguments[index]!, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The plain digits with no trailing zeros, as decimal.js's <c>toFixed()</c> prints them.</summary>
    private static string Text(decimal value) =>
        value.ToString(
            "0.############################",
            System.Globalization.CultureInfo.InvariantCulture
        );
}
