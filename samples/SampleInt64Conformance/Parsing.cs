using System.Globalization;
using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The parsing rows of the reference matrix (spec 012 FR-038,
/// contracts/runtime-int64.md § Parsing): <c>Parse</c> and <c>TryParse</c>
/// over every text operand, for each supported <c>NumberStyles</c>.
/// </summary>
[NoContainer]
public static class Parsing
{
    public static long Parse(string text) => long.Parse(text);

    public static long ParseHex(string text) => long.Parse(text, NumberStyles.HexNumber);

    public static long ParseThousands(string text) =>
        long.Parse(text, NumberStyles.Integer | NumberStyles.AllowThousands);

    public static long ParseNumber(string text) => long.Parse(text, NumberStyles.Number);

    public static ulong ParseUnsigned(string text) => ulong.Parse(text);

    public static ulong ParseUnsignedHex(string text) => ulong.Parse(text, NumberStyles.HexNumber);

    public static ulong ParseUnsignedNumber(string text) =>
        ulong.Parse(text, NumberStyles.Number, null);

    public static long? TryParse(string text) => long.TryParse(text, out var value) ? value : null;

    public static ulong? TryParseUnsignedNumber(string text) =>
        ulong.TryParse(text, NumberStyles.Number, null, out var value) ? value : null;

    public static long TryParseFailureValue(string text)
    {
        long.TryParse(text, NumberStyles.HexNumber, null, out var value);

        return value;
    }
}
