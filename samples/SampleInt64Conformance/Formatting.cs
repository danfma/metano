using Metano.Annotations;

namespace SampleInt64Conformance;

/// <summary>
/// The formatting rows of the reference matrix (spec 012 FR-037,
/// contracts/runtime-int64.md § Formatting): <c>ToString(format)</c> over
/// every format operand, and interpolation holes with an alignment and a
/// format, in the invariant culture.
/// </summary>
[NoContainer]
public static class Formatting
{
    public static string Format(long value, string format) => value.ToString(format);

    public static string FormatUnsigned(ulong value, string format) => value.ToString(format);

    public static string Interpolate(long value) =>
        $"[{value, 8}][{value, -8}][{value:N0}][{value, 24:X}][{value:D3}][{value}]";

    public static string InterpolateUnsigned(ulong value) => $"[{value, 25:N0}][{value:x}]";
}
