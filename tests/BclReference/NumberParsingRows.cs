using System.Globalization;

namespace BclReference;

/// <summary>
/// The rows for <c>Parse</c> / <c>TryParse</c> of the integer types of 32 bits and fewer,
/// <c>double</c> and <c>bool</c> (#668).
/// </summary>
internal static class NumberParsingRows
{
    private const int Integer = 7;
    private const int HexNumber = 515;
    private const int IntegerWithThousands = 71;
    private const int Number = 111;

    private static readonly string?[] DecimalTexts =
    [
        "0",
        "-0",
        "7",
        "+7",
        "-7",
        " 42 ",
        "\t42\n",
        "42\0",
        "",
        " ",
        null,
        "abc",
        "12abc",
        "1.5",
        "1.0",
        "1,000",
        "1,0",
        "-",
        "+",
        "--1",
        "+-1",
        "1-",
        "(5)",
        "1e3",
        "0x10",
        "٣",
        "127",
        "128",
        "-128",
        "-129",
        "255",
        "256",
        "-1",
        "32767",
        "32768",
        "-32768",
        "-32769",
        "65535",
        "65536",
        "2147483647",
        "2147483648",
        "-2147483648",
        "-2147483649",
        "4294967295",
        "4294967296",
        "00000000000000000000000000042",
    ];

    private static readonly string?[] HexTexts =
    [
        "0",
        "f",
        "FF",
        "ff",
        "7f",
        "80",
        "100",
        "7fff",
        "8000",
        "ffff",
        "10000",
        "7fffffff",
        "80000000",
        "ffffffff",
        "100000000",
        "00000000ff",
        " ff ",
        "",
        null,
        "xyz",
        "0xff",
        "-1",
        "+1",
        "1 f",
    ];

    public static void AddTo(FixtureWriter rows)
    {
        AddIntegers(rows);
        AddFloatingPointAndBoolean(rows);
    }

    private static void AddIntegers(FixtureWriter rows)
    {
        AddInteger(
            rows,
            "Int32",
            text => int.Parse(text),
            (text, style) => int.Parse(text, style, CultureInfo.InvariantCulture),
            text => Outcome(int.TryParse(text, out var value), value),
            (text, style) =>
                Outcome(
                    int.TryParse(text, style, CultureInfo.InvariantCulture, out var value),
                    value
                )
        );

        AddInteger(
            rows,
            "UInt32",
            text => uint.Parse(text),
            (text, style) => uint.Parse(text, style, CultureInfo.InvariantCulture),
            text => Outcome(uint.TryParse(text, out var value), value),
            (text, style) =>
                Outcome(
                    uint.TryParse(text, style, CultureInfo.InvariantCulture, out var value),
                    value
                )
        );

        AddInteger(
            rows,
            "Int16",
            text => short.Parse(text),
            (text, style) => short.Parse(text, style, CultureInfo.InvariantCulture),
            text => Outcome(short.TryParse(text, out var value), value),
            (text, style) =>
                Outcome(
                    short.TryParse(text, style, CultureInfo.InvariantCulture, out var value),
                    value
                )
        );

        AddInteger(
            rows,
            "UInt16",
            text => ushort.Parse(text),
            (text, style) => ushort.Parse(text, style, CultureInfo.InvariantCulture),
            text => Outcome(ushort.TryParse(text, out var value), value),
            (text, style) =>
                Outcome(
                    ushort.TryParse(text, style, CultureInfo.InvariantCulture, out var value),
                    value
                )
        );

        AddInteger(
            rows,
            "Byte",
            text => byte.Parse(text),
            (text, style) => byte.Parse(text, style, CultureInfo.InvariantCulture),
            text => Outcome(byte.TryParse(text, out var value), value),
            (text, style) =>
                Outcome(
                    byte.TryParse(text, style, CultureInfo.InvariantCulture, out var value),
                    value
                )
        );

        AddInteger(
            rows,
            "SByte",
            text => sbyte.Parse(text),
            (text, style) => sbyte.Parse(text, style, CultureInfo.InvariantCulture),
            text => Outcome(sbyte.TryParse(text, out var value), value),
            (text, style) =>
                Outcome(
                    sbyte.TryParse(text, style, CultureInfo.InvariantCulture, out var value),
                    value
                )
        );
    }

    private static void AddInteger(
        FixtureWriter rows,
        string name,
        Func<string, object> parse,
        Func<string, NumberStyles, object> parseStyled,
        Func<string, object> tryParse,
        Func<string, NumberStyles, object> tryParseStyled
    )
    {
        object?[][] plain = [.. DecimalTexts.Select(text => new object?[] { text })];

        object?[][] styled =
        [
            .. DecimalTexts.SelectMany(text =>
                new[] { Integer, IntegerWithThousands, Number }.Select(style =>
                    new object?[] { text, style }
                )
            ),
            .. HexTexts.Select(text => new object?[] { text, HexNumber }),
        ];

        rows.Each($"parse{name}", a => parse((string)a[0]!), plain);

        rows.Each(
            $"parse{name}",
            a => parseStyled((string)a[0]!, (NumberStyles)(int)a[1]!),
            styled
        );

        rows.Each($"tryParse{name}", a => tryParse((string)a[0]!), plain);

        rows.Each(
            $"tryParse{name}",
            a => tryParseStyled((string)a[0]!, (NumberStyles)(int)a[1]!),
            styled
        );
    }

    private static object Outcome(bool found, object value) => new[] { found, value };

    private static void AddFloatingPointAndBoolean(FixtureWriter rows)
    {
        string?[] doubles =
        [
            "0",
            "-0",
            "1.5",
            " 2.5 ",
            "1e3",
            "1E-3",
            "1,234.5",
            ".5",
            "5.",
            "",
            null,
            "abc",
            "1.2.3",
            "NaN",
            "nan",
            "Infinity",
            "-Infinity",
            "infinity",
            "1e400",
            "-1e400",
            "+.5e+1",
            "0x10",
            "1 2",
            "--1",
            "constructor",
        ];

        rows.Each(
            "parseFloat64",
            a => double.Parse((string)a[0]!, CultureInfo.InvariantCulture),
            [.. doubles.Select(text => new object?[] { text })]
        );

        string?[] booleans =
        [
            "true",
            "false",
            "True",
            "FALSE",
            " true ",
            "\ttrue\n",
            "tRuE",
            "",
            null,
            "yes",
            "1",
            "0",
            "truee",
            "t",
        ];

        rows.Each(
            "parseBoolean",
            a => bool.Parse((string)a[0]!),
            [.. booleans.Select(text => new object?[] { text })]
        );
    }
}
