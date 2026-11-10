using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// How a <c>number</c> is brought into the domain of an integer of 32 bits
/// or fewer (spec 012 FR-074–FR-077), shared by the conversion and overflow
/// bridges. Unchecked, the native 32-bit operators keep the low bits and
/// import nothing: <c>x | 0</c> (<c>int</c>), <c>x &gt;&gt;&gt; 0</c>
/// (<c>uint</c>), <c>(x &lt;&lt; 16) &gt;&gt; 16</c> (<c>short</c>),
/// <c>x &amp; 0xFFFF</c> (<c>ushort</c>), <c>(x &lt;&lt; 24) &gt;&gt; 24</c>
/// (<c>sbyte</c>) and <c>x &amp; 0xFF</c> (<c>byte</c>). Checked, a
/// <c>metano-runtime</c> helper returns the value or throws
/// <c>OverflowException</c>.
/// </summary>
internal static class NarrowIntegerTsExpressions
{
    private const string ByteMask = "0xFF";
    private const string UInt16Mask = "0xFFFF";
    private const int Int32Width = 32;

    /// <summary><paramref name="value"/> reduced to <paramref name="kind"/>: wrapped, or range-checked when <paramref name="isChecked"/>.</summary>
    public static TsExpression Reduced(IrNumericKind kind, bool isChecked, TsExpression value) =>
        isChecked ? Checked(kind, value) : Wrapped(kind, value);

    /// <summary>The low bits of <paramref name="value"/>, read as <paramref name="kind"/>.</summary>
    public static TsExpression Wrapped(IrNumericKind kind, TsExpression value) =>
        kind switch
        {
            IrNumericKind.Int32 => Operator(value, "|", "0"),
            IrNumericKind.UInt32 => Operator(value, ">>>", "0"),
            IrNumericKind.SByte or IrNumericKind.Int16 => SignExtended(value, kind.BitWidth()),
            IrNumericKind.Byte => Operator(value, "&", ByteMask),
            IrNumericKind.UInt16 or IrNumericKind.Char => Operator(value, "&", UInt16Mask),
            _ => throw NotNarrow(kind),
        };

    /// <summary><paramref name="value"/> when it fits <paramref name="kind"/>; <c>OverflowException</c> otherwise.</summary>
    public static TsExpression Checked(IrNumericKind kind, TsExpression value) =>
        kind switch
        {
            IrNumericKind.Int32 => RuntimeCall(Int64RuntimeHelpers.CheckedInt32, value),
            IrNumericKind.UInt32 => RuntimeCall(Int64RuntimeHelpers.CheckedUInt32, value),
            IrNumericKind.SByte
            or IrNumericKind.Byte
            or IrNumericKind.Int16
            or IrNumericKind.UInt16 => RuntimeCall(
                Int64RuntimeHelpers.CheckedNarrowInt32,
                value,
                Literal(kind.BitWidth()),
                new TsLiteral(kind.IsSigned() ? "true" : "false")
            ),
            _ => throw NotNarrow(kind),
        };

    /// <summary><c>(value &lt;&lt; shift) &gt;&gt; shift</c>: the low <paramref name="bits"/> bits, sign-extended.</summary>
    private static TsExpression SignExtended(TsExpression value, int bits)
    {
        var count = Literal(Int32Width - bits);

        return new TsBinaryExpression(
            new TsParenthesized(new TsBinaryExpression(Grouped(value), "<<", count)),
            ">>",
            count
        );
    }

    private static TsExpression Operator(TsExpression value, string token, string operand) =>
        new TsBinaryExpression(Grouped(value), token, new TsLiteral(operand));

    /// <summary>
    /// A binary or conditional expression keeps explicit parentheses as an operand:
    /// <c>(b + 1) &amp; 0xFF</c> reads as it groups, where the grammar-minimal
    /// <c>b + 1 &amp; 0xFF</c> does not.
    /// </summary>
    private static TsExpression Grouped(TsExpression value) =>
        value is TsBinaryExpression or TsConditionalExpression ? new TsParenthesized(value) : value;

    private static TsLiteral Literal(int value) =>
        new(value.ToString(CultureInfo.InvariantCulture));

    private static InvalidOperationException NotNarrow(IrNumericKind kind) =>
        new($"'{kind}' is not an integer of 32 bits or fewer.");
}
