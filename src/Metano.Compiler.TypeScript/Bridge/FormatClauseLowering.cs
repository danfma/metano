using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers the format and alignment clauses of an interpolation hole
/// (baseline FR-051, #323) to one <c>metano-runtime</c> call inside the
/// template literal, picked by the value's static type so a project that
/// writes no clause imports no formatter: <c>formatInteger</c> for an integer
/// of 32 bits or fewer, <c>formatInt64</c> for a 64-bit one
/// (<see cref="Int64Formatting"/>), <c>formatDouble</c> for a <c>double</c>,
/// and <c>alignValue</c> for an alignment without a format, which pads the
/// text the hole prints without the clause. The core has already reported
/// every clause the runtime does not format.
/// </summary>
internal static class FormatClauseLowering
{
    private const string FormatInteger = "formatInteger";

    private const string FormatDouble = "formatDouble";

    private const string AlignValue = "alignValue";

    /// <summary>The runtime call for a hole with a clause; <c>null</c> for a hole without one.</summary>
    public static TsExpression? TryLowerHole(IrInterpolationExpression hole, TsExpression value)
    {
        if (!hole.HasClause)
            return null;

        if (Int64Formatting.TryFormatHole(hole, value) is { } sixtyFourBit)
            return sixtyFourBit;

        return hole.FormatSpecifier is { } format
            ? FormatNumber(hole, value, format)
            : RuntimeCall(AlignValue, value, Number(hole.Alignment!.Value));
    }

    private static TsExpression FormatNumber(
        IrInterpolationExpression hole,
        TsExpression value,
        string format
    )
    {
        TsExpression[] formatAndAlignment = hole.Alignment is { } alignment
            ? [new TsStringLiteral(format), Number(alignment)]
            : [new TsStringLiteral(format)];

        return hole.NumericKind switch
        {
            IrNumericKind.Double => RuntimeCall(FormatDouble, [value, .. formatAndAlignment]),
            { } kind when BitsOf(kind) is { } bits => RuntimeCall(
                FormatInteger,
                [value, Number(bits), .. formatAndAlignment]
            ),
            _ => throw new InvalidOperationException(
                $"The core keeps the format '{format}' only on a hole the runtime formats (FR-051)."
            ),
        };
    }

    /// <summary>The bit width <c>formatInteger</c> prints a negative hex value on, or <c>null</c>.</summary>
    private static int? BitsOf(IrNumericKind kind) =>
        kind switch
        {
            IrNumericKind.SByte or IrNumericKind.Byte => 8,
            IrNumericKind.Int16 or IrNumericKind.UInt16 => 16,
            IrNumericKind.Int32 or IrNumericKind.UInt32 => 32,
            _ => null,
        };

    private static TsLiteral Number(int value) => new(value.ToString(CultureInfo.InvariantCulture));

    private static TsCallExpression RuntimeCall(string name, params TsExpression[] arguments) =>
        Int64TsExpressions.RuntimeCall(name, arguments);
}
