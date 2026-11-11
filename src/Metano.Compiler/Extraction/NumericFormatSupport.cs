using System.Text.RegularExpressions;
using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The .NET standard numeric format strings the TypeScript runtime formats
/// with invariant-culture parity (baseline FR-051): <c>formatInteger</c> /
/// <c>formatInt64</c> for the integers, <c>formatDouble</c> for
/// <c>double</c>, both in <c>metano-runtime</c>. The sets mirror those
/// modules.
/// </summary>
internal static partial class NumericFormatSupport
{
    /// <summary>The integer format .NET defines that the runtime does not implement (binary).</summary>
    private const char UnimplementedIntegerFormat = 'B';

    /// <summary>
    /// Whether values of <paramref name="kind"/> have a runtime formatter: the
    /// integers (not <c>char</c>) and <c>double</c>. <c>float</c> does not:
    /// its value is not held at single precision, so its formatted digits
    /// would differ from .NET's.
    /// </summary>
    public static bool HasFormatter(IrNumericKind kind) =>
        kind
            is IrNumericKind.SByte
                or IrNumericKind.Byte
                or IrNumericKind.Int16
                or IrNumericKind.UInt16
                or IrNumericKind.Int32
                or IrNumericKind.UInt32
                or IrNumericKind.Int64
                or IrNumericKind.UInt64
                or IrNumericKind.Double;

    /// <summary>
    /// Whether the formatter of <paramref name="kind"/> formats
    /// <paramref name="format"/> as .NET does: the empty format, or one letter
    /// and an optional precision other than an integer's <c>B</c>. A letter
    /// .NET rejects (<c>Q</c>, <c>D</c> on a <c>double</c>) counts, since the
    /// runtime throws the same <c>FormatException</c>; a custom format
    /// (<c>0.00</c>, <c>#,##0</c>) does not. The 64-bit integers accept every
    /// format here: MS0066 reports theirs (spec 012 FR-037).
    /// </summary>
    public static bool Supports(IrNumericKind kind, string format)
    {
        if (kind is IrNumericKind.Int64 or IrNumericKind.UInt64 || format.Length == 0)
            return true;

        var isUnimplemented =
            kind is not IrNumericKind.Double
            && char.ToUpperInvariant(format[0]) == UnimplementedIntegerFormat;

        return StandardFormat().IsMatch(format) && !isUnimplemented;
    }

    [GeneratedRegex("^[A-Za-z][0-9]*$")]
    private static partial Regex StandardFormat();
}
