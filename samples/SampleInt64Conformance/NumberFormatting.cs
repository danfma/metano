using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The interpolation format and alignment clauses, and <c>ToString</c> with a
/// constant format, on the numbers held in a JS <c>number</c> (baseline
/// FR-051, #323): each hole calls the runtime formatter of its value's type.
/// The runtime formatters themselves run over every format operand through
/// <c>tests/Int64Reference/RuntimeFormatting.cs</c>; a format that is not a
/// constant reports MS0001 when transpiled, so those rows have no transpiled
/// twin. An alignment without a format pads the text the hole prints without
/// the clause, which for a <c>double</c> is still JavaScript's (#308), so the
/// <c>double</c> holes below all carry a format.
/// </summary>
[NoContainer]
public static class NumberFormatting
{
    public static string InterpolateDouble(double formatted) =>
        $"[{formatted, 12:F2}][{formatted:N0}][{formatted, -14:E3}][{formatted:P1}][{formatted:G5}][{formatted:C}][{formatted:R}][{formatted:g17}][{formatted:F0}][{formatted:e}]";

    public static string InterpolateNullableDouble(double? formatted) =>
        $"[{formatted, 8:F1}][{formatted:N}]";

    public static string InterpolateInt32(int formatted) =>
        $"[{formatted, 8}][{formatted:D5}][{formatted:X}][{formatted, -16:N0}][{formatted:E2}][{formatted:P0}][{formatted:C}][{formatted:G2}][{formatted:x4}]";

    public static string InterpolateNullableInt32(int? formatted) =>
        $"[{formatted, 4}][{formatted:D2}]";

    public static string InterpolateUInt32(uint formatted) => $"[{formatted:X}][{formatted:N0}]";

    public static string InterpolateInt16(short formatted) => $"[{formatted:X}][{formatted, 7:D6}]";

    public static string InterpolateUInt16(ushort formatted) => $"[{formatted:x}][{formatted:E1}]";

    public static string InterpolateSByte(sbyte formatted) => $"[{formatted:X2}][{formatted:D4}]";

    public static string InterpolateByte(byte formatted) => $"[{formatted:x}][{formatted, 9:P0}]";

    public static string FormatDoubleConstant(double formatted) =>
        formatted.ToString("N3") + "|" + formatted.ToString("E", null);

    public static string FormatInt32Constant(int formatted) =>
        formatted.ToString("D3") + "|" + formatted.ToString("X8");
}
