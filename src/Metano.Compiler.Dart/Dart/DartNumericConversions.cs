using System.Globalization;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart;

/// <summary>
/// How Dart prints the numeric IR nodes of spec 012, given that a Dart
/// <c>int</c> is already a 64-bit two's-complement value (DECIDED-19).
/// A 64-bit <see cref="IrIntegerOverflow"/> prints as its operand (the VM
/// <c>int</c> wraps by itself, and a checked context is a documented gap);
/// one narrower than 64 bits keeps the low bits with a selector
/// (<see cref="OverflowSelector"/>). An
/// <see cref="IrNumericConversion"/> that changes the value prints as a
/// method selector on its operand (<see cref="ConversionSelector"/>); one
/// Dart does not convert keeps the explicit cast it lowers
/// (<c>x as T</c>, as before the conversion node existed), or prints its
/// operand when it is implicit. The printer and the operand-grouping rules
/// read every node through <see cref="Unwrap"/>, so a wrapped operand keeps
/// the parentheses its own shape needs.
/// </summary>
internal static class DartNumericConversions
{
    /// <summary>The expression Dart actually prints for <paramref name="expression"/>.</summary>
    public static IrExpression Unwrap(IrExpression expression) =>
        expression switch
        {
            IrIntegerOverflow overflow when OverflowSelector(overflow) is null => Unwrap(
                overflow.Value
            ),
            IrNumericConversion conversion when ConversionSelector(conversion) is null =>
                conversion.CastType is { } castType
                    ? new IrCastExpression(conversion.Operand, castType)
                    : Unwrap(conversion.Operand),
            _ => expression,
        };

    /// <summary>
    /// The selector Dart appends to the operand of <paramref name="conversion"/>:
    /// <c>.toInt()</c> out of a floating-point value, <c>.toDouble()</c> into
    /// one, and <c>.toSigned(n)</c> / <c>.toUnsigned(n)</c> to keep the low
    /// <c>n</c> bits of an integer narrower than 64 bits
    /// (<c>(int)0x100000001L</c> is <c>1</c>). <c>null</c> when Dart does not
    /// convert: a 64-bit-preserving conversion, and the <c>char</c>,
    /// <c>decimal</c>, <c>BigInteger</c> and enum endpoints this shape-only
    /// target leaves to the cast (documented gaps). A lifted conversion opens
    /// the chain with <c>?.</c>, so <c>null</c> passes through.
    /// </summary>
    public static string? ConversionSelector(IrNumericConversion conversion)
    {
        var chain = SelectorChain(conversion.From, conversion.To);

        // A lifted conversion (`int?` → `long?`) short-circuits on `null`.
        return chain is not null && conversion.IsLifted ? "?" + chain : chain;
    }

    /// <summary>
    /// The selector Dart appends to the value of <paramref name="overflow"/>:
    /// <c>.toSigned(n)</c> / <c>.toUnsigned(n)</c> for a kind narrower than
    /// 64 bits, whose result a Dart <c>int</c> holds unreduced
    /// (<c>(b + 1).toUnsigned(8)</c>, spec 012 FR-075–FR-077). <c>null</c>,
    /// so the value prints as it is, for a 64-bit kind, which the VM
    /// <c>int</c> already wraps, and for checked <c>int</c> / <c>uint</c>
    /// arithmetic: a checked context is a documented gap in Dart
    /// (DECIDED-19), so the operation prints as its unchecked form, which
    /// does not wrap either. A checked store narrower than <c>int</c> keeps
    /// the narrowing of its unchecked form.
    /// </summary>
    public static string? OverflowSelector(IrIntegerOverflow overflow) =>
        overflow.Kind.IsSixtyFourBit() || IsCheckedThirtyTwoBitArithmetic(overflow)
            ? null
            : Narrowing(overflow.Kind);

    /// <summary>
    /// The left operand selector of <paramref name="binary"/>: <c>&gt;&gt;&gt;</c>
    /// on an <c>int</c> shifts its 32 bits, where a Dart <c>int</c> holds a
    /// negative value sign-extended to 64, so the operand is read as
    /// unsigned first (<c>i.toUnsigned(32) &gt;&gt;&gt; n</c>). <c>null</c>
    /// for any other operator.
    /// </summary>
    public static string? LeftOperandSelector(IrBinaryExpression binary) =>
        binary is { Operator: IrBinaryOp.UnsignedRightShift, NumericKind: IrNumericKind.Int32 }
            ? Narrowing(IrNumericKind.UInt32)
            : null;

    private static bool IsCheckedThirtyTwoBitArithmetic(IrIntegerOverflow overflow) =>
        overflow.Mode is IrOverflowMode.Check && overflow.Kind.IsThirtyTwoBit();

    private static string? SelectorChain(IrNumericEndpoint from, IrNumericEndpoint to)
    {
        if (!IsConvertible(from) || !IsConvertible(to))
            return null;

        if (to.Kind.IsFloatingPoint())
            return from.Kind.IsFloatingPoint() ? null : ".toDouble()";

        var narrowing = to.Kind.IsThirtyTwoBitOrNarrower() ? Narrowing(to.Kind) : null;

        return from.Kind.IsFloatingPoint() ? ".toInt()" + narrowing : narrowing;
    }

    private static string Narrowing(IrNumericKind kind) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $".{(kind.IsSigned() ? "toSigned" : "toUnsigned")}({kind.BitWidth()})"
        );

    private static bool IsConvertible(IrNumericEndpoint endpoint) =>
        !endpoint.IsEnum && (endpoint.Kind.IsInteger() || endpoint.Kind.IsFloatingPoint());
}
