namespace BclReference;

/// <summary>
/// The rows for the parse helpers of <c>metano-runtime</c>: <c>Guid.Parse</c> and
/// <c>decimal.TryParse</c> (#673).
/// </summary>
internal static class ParsingRows
{
    public static void AddTo(FixtureWriter rows)
    {
        AddGuidParse(rows);
        AddDecimalTryParse(rows);
    }

    private static void AddGuidParse(FixtureWriter rows)
    {
        rows.Each(
            "uuidParse",
            a => Guid.Parse((string)a[0]!).ToString(),
            ["0F8FAD5B-D9CB-469F-A165-70867728950E"],
            ["0f8fad5b-d9cb-469f-a165-70867728950e"],
            ["0F8FAD5BD9CB469FA16570867728950E"],
            ["{0F8FAD5B-D9CB-469F-A165-70867728950E}"],
            ["(0F8FAD5B-D9CB-469F-A165-70867728950E)"],
            ["  0F8FAD5B-D9CB-469F-A165-70867728950E  "],
            ["00000000-0000-0000-0000-000000000000"],
            ["nope"],
            [""],
            ["   "],
            [null],
            ["{0F8FAD5B-D9CB-469F-A165-70867728950E)"],
            ["0F8FAD5B-D9CB-469F-A165-70867728950"],
            ["0F8FAD5B-D9CB-469F-A165-70867728950EF"],
            ["0F8FAD5BD9CB469FA16570867728950"],
            ["0F8FAD5B-D9CB-469F-A165-7086772895GE"]
        );
    }

    private static void AddDecimalTryParse(FixtureWriter rows)
    {
        rows.Each(
            "tryParseDecimal",
            a =>
            {
                var found = decimal.TryParse((string?)a[0], out var value);

                return new object[] { found, (value / 1.0000000000000000000000000000m).ToString() };
            },
            ["1.5"],
            ["x"],
            [null],
            [""],
            ["1e3"],
            ["79228162514264337593543950335"],
            ["79228162514264337593543950336"],
            [" -1,234.50 "],
            ["1.5-"],
            ["+1.5"],
            ["-.5"],
            ["1."],
            ["."],
            ["1.10"],
            ["0.0000000000000000000000000001"],
            ["1 2"],
            ["--1"]
        );
    }
}
