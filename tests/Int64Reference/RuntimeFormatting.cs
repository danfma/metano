namespace Int64Reference;

/// <summary>
/// The .NET side of the runtime formatter parity (baseline FR-051, #323):
/// <c>ToString(format)</c> over every format operand of the standard numeric
/// formats, for <c>double</c> and the integers held in a JS <c>number</c>.
/// Its TypeScript twin calls <c>formatDouble</c> / <c>formatInteger</c>
/// directly (<c>targets/js/sample-int64-conformance/test/runtime-formatters.ts</c>):
/// transpiled, a format that is not a constant reports MS0001 and throws.
/// </summary>
internal static class RuntimeFormatting
{
    public static string FormatDouble(double formatted, string numberFormat) =>
        formatted.ToString(numberFormat);

    public static string FormatInt32(int formatted, string numberFormat) =>
        formatted.ToString(numberFormat);

    public static string FormatUInt32(uint formatted, string numberFormat) =>
        formatted.ToString(numberFormat);

    public static string FormatInt16(short formatted, string numberFormat) =>
        formatted.ToString(numberFormat);

    public static string FormatUInt16(ushort formatted, string numberFormat) =>
        formatted.ToString(numberFormat);

    public static string FormatSByte(sbyte formatted, string numberFormat) =>
        formatted.ToString(numberFormat);

    public static string FormatByte(byte formatted, string numberFormat) =>
        formatted.ToString(numberFormat);
}
