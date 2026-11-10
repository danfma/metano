using Metano.Compiler.IR;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The overflow reductions of the integers of 32 bits or fewer (spec 012
/// FR-075–FR-077), which the targets hold in a type wider than the C# one
/// (a JavaScript <c>number</c>, a 64-bit Dart <c>int</c>):
/// <list type="bullet">
///   <item>the value a compound assignment or an increment stores in an
///   <c>sbyte</c>, <c>byte</c>, <c>short</c> or <c>ushort</c> is narrowed
///   as the conversion C# inserts narrows it (<c>b += 1</c> is
///   <c>b = (byte)(b + 1)</c>), wrapped or range-checked;</item>
///   <item>the <c>uint</c> result of <c>&amp;</c>, <c>|</c>, <c>^</c>,
///   <c>~</c> and <c>&lt;&lt;</c>, and the <c>int</c> result of
///   <c>&gt;&gt;&gt;</c>, wrap in either context: they cannot overflow, but
///   a target whose bitwise operators compute in another signedness reads
///   them back through the wrap;</item>
///   <item><c>+</c>, <c>-</c>, <c>*</c>, unary <c>-</c>, <c>++</c> and
///   <c>--</c> on <c>int</c> / <c>uint</c> are range-checked in a checked
///   context. In an unchecked context they are not reduced: the wrap of
///   <c>int</c> / <c>uint</c> arithmetic is a documented gap.</item>
/// </list>
/// A reduction that cannot change the value is not produced: the operand
/// ranges (<see cref="IntegerValueRanges"/>) decide.
/// </summary>
internal static class NarrowIntegerOverflow
{
    private static readonly IntegerRange Int32Range = IntegerRange
        .Of(IrNumericKind.Int32)
        .GetValueOrDefault();

    /// <summary>
    /// The reduction of a built-in binary operator computing in
    /// <paramref name="kind"/> (<c>int</c> or <c>uint</c>, the types C#
    /// promotes narrower operands to).
    /// </summary>
    public static IntegerOverflowReduction? BinaryReduction(
        BinaryOperatorKind operatorKind,
        IrNumericKind kind,
        bool isChecked,
        IntegerRange? left,
        IntegerRange? right
    ) =>
        operatorKind switch
        {
            BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply =>
                CheckedArithmetic(
                    kind,
                    isChecked,
                    IntegerValueRanges.ExactResult(operatorKind, left, right)
                ),
            BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr =>
                UnsignedBits(kind, IntegerValueRanges.ExactResult(operatorKind, left, right)),
            BinaryOperatorKind.LeftShift => UnsignedBits(kind, result: null),
            BinaryOperatorKind.UnsignedRightShift => SignedBits(kind, left),
            _ => null,
        };

    /// <summary>
    /// The reduction of <c>target op= value</c> on a target of
    /// <paramref name="targetKind"/>. An <c>sbyte</c>, <c>byte</c>,
    /// <c>short</c> or <c>ushort</c> target stores the <c>int</c> result
    /// narrowed, unless the operator keeps it in the target's range; an
    /// <c>int</c> / <c>uint</c> target follows <see cref="BinaryReduction"/>.
    /// </summary>
    public static IntegerOverflowReduction? CompoundReduction(
        BinaryOperatorKind operatorKind,
        IrNumericKind targetKind,
        bool isChecked,
        IntegerRange? value
    )
    {
        if (targetKind.BitWidth() == 32)
        {
            return BinaryReduction(
                operatorKind,
                targetKind,
                isChecked,
                IntegerRange.Of(targetKind),
                value
            );
        }

        if (StaysInOperandRange(operatorKind, targetKind.IsSigned()))
            return null;

        return IntegerOverflowReduction.Of(targetKind, isChecked) with
        {
            Operator = OperatorResultRead(operatorKind, isChecked),
        };
    }

    /// <summary>
    /// The reduction of the <c>int</c> result a narrowing store reads. A
    /// checked store range-checks the value of <c>&gt;&gt;&gt;</c> as the
    /// <c>int</c> C# computed (<c>checked { s &gt;&gt;&gt;= 0; }</c> keeps a
    /// negative <c>short</c>), so it is read back as signed first; a
    /// wrapping store keeps the same low bits either way.
    /// </summary>
    private static IntegerOverflowReduction? OperatorResultRead(
        BinaryOperatorKind operatorKind,
        bool isChecked
    ) =>
        isChecked && operatorKind is BinaryOperatorKind.UnsignedRightShift
            ? IntegerOverflowReduction.Of(IrNumericKind.Int32, isChecked: false)
            : null;

    /// <summary>
    /// <c>++</c> / <c>--</c>: an <c>sbyte</c>, <c>byte</c>, <c>short</c> or
    /// <c>ushort</c> stores the stepped value narrowed; an <c>int</c> /
    /// <c>uint</c> is range-checked in a checked context only.
    /// </summary>
    public static IntegerOverflowReduction? IncrementReduction(IrNumericKind kind, bool isChecked)
    {
        if (kind.BitWidth() < 32)
            return IntegerOverflowReduction.Of(kind, isChecked);

        return isChecked ? IntegerOverflowReduction.Of(kind, isChecked: true) : null;
    }

    /// <summary>
    /// Unary <c>-</c> on an <c>int</c> overflows for <c>int.MinValue</c>
    /// only (on a <c>uint</c> C# computes in <c>long</c>); <c>~</c> on a
    /// <c>uint</c> is read back as unsigned.
    /// </summary>
    public static IntegerOverflowReduction? UnaryReduction(
        UnaryOperatorKind operatorKind,
        IrNumericKind kind,
        bool isChecked,
        IntegerRange? operand
    ) =>
        operatorKind switch
        {
            UnaryOperatorKind.Minus => CheckedArithmetic(kind, isChecked, operand?.Negated()),
            UnaryOperatorKind.BitwiseNegation => UnsignedBits(kind, result: null),
            _ => null,
        };

    /// <summary>
    /// A range check of <paramref name="kind"/> in a checked context, unless
    /// the exact <paramref name="result"/> is known to fit.
    /// </summary>
    private static IntegerOverflowReduction? CheckedArithmetic(
        IrNumericKind kind,
        bool isChecked,
        IntegerRange? result
    )
    {
        if (!isChecked)
            return null;

        var fits =
            result is { } exact && IntegerRange.Of(kind) is { } domain && domain.Contains(exact);

        return fits ? null : IntegerOverflowReduction.Of(kind, isChecked: true);
    }

    /// <summary>
    /// A <c>uint</c> bit pattern read back as unsigned, unless the
    /// <paramref name="result"/> is known to keep bit 31 clear, where the
    /// signed and the unsigned reading agree.
    /// </summary>
    private static IntegerOverflowReduction? UnsignedBits(IrNumericKind kind, IntegerRange? result)
    {
        if (kind is not IrNumericKind.UInt32)
            return null;

        var keepsSignBitClear = result is { } exact && Int32Range.Contains(exact);

        return keepsSignBitClear ? null : IntegerOverflowReduction.Of(kind, isChecked: false);
    }

    /// <summary>
    /// The <c>int</c> result of <c>&gt;&gt;&gt;</c> read back as signed
    /// (a count of <c>0</c> keeps a negative value), unless the shifted
    /// value is known to be non-negative.
    /// </summary>
    private static IntegerOverflowReduction? SignedBits(IrNumericKind kind, IntegerRange? shifted)
    {
        if (kind is not IrNumericKind.Int32)
            return null;

        return shifted is { IsNonNegative: true }
            ? null
            : IntegerOverflowReduction.Of(kind, isChecked: false);
    }

    /// <summary>
    /// Whether <c>a op b</c>, both operands in the range of one integer type
    /// narrower than <c>int</c>, stays in that range: the bitwise operators,
    /// <c>&gt;&gt;</c> and <c>%</c> always; <c>/</c> and <c>&gt;&gt;&gt;</c>
    /// for an unsigned type (<c>sbyte.MinValue / -1</c> is <c>128</c>, and
    /// <c>&gt;&gt;&gt;</c> reads a negative value as a large positive one).
    /// </summary>
    private static bool StaysInOperandRange(BinaryOperatorKind operatorKind, bool isSigned) =>
        operatorKind switch
        {
            BinaryOperatorKind.And
            or BinaryOperatorKind.Or
            or BinaryOperatorKind.ExclusiveOr
            or BinaryOperatorKind.RightShift
            or BinaryOperatorKind.Remainder => true,
            BinaryOperatorKind.Divide or BinaryOperatorKind.UnsignedRightShift => !isSigned,
            _ => false,
        };
}
