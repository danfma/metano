using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <see cref="IntegerRange"/> of the values a Roslyn operation can
/// produce, read from what Roslyn already knows: a constant is its value, a
/// conversion that cannot change the value keeps its operand's range, a
/// built-in <c>+</c>, <c>-</c>, <c>*</c>, <c>&amp;</c>, <c>|</c>, <c>^</c>
/// or <c>&lt;&lt;</c> by a constant combines its operands' ranges, and
/// anything else spans its type. It is
/// not a data-flow analysis: a variable spans its type.
/// </summary>
internal static class IntegerValueRanges
{
    /// <summary>
    /// The values <paramref name="operation"/> can produce; <c>null</c> when
    /// its type is not an integer of 64 bits or fewer (an enum reads as its
    /// underlying kind).
    /// </summary>
    public static IntegerRange? Of(IOperation? operation)
    {
        if (operation is null)
            return null;

        if (ConstantOf(operation) is { } constant)
            return IntegerRange.Exactly(constant);

        if (OfType(operation.Type) is not { } typeRange)
            return null;

        var exact = operation switch
        {
            IConversionOperation { Conversion.IsUserDefined: false } conversion => Of(
                conversion.Operand
            ),
            IBinaryOperation { OperatorMethod: null } binary => ExactResult(
                binary.OperatorKind,
                Of(binary.LeftOperand),
                Of(binary.RightOperand)
            ),
            _ => null,
        };

        // A value outside the type's range wraps (or throws), so it says
        // nothing beyond the type itself.
        return exact is { } range && typeRange.Contains(range) ? range : typeRange;
    }

    /// <summary>The values of the integer type <paramref name="type"/>, through <c>Nullable&lt;T&gt;</c>; <c>null</c> for any other type.</summary>
    public static IntegerRange? OfType(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { Kind: var kind } && kind.IsInteger()
            ? IntegerRange.Of(kind)
            : null;

    /// <summary>
    /// The mathematical result of the built-in operator <paramref name="kind"/>
    /// over operands in <paramref name="left"/> and <paramref name="right"/>,
    /// before any wrap; <c>null</c> when an operand range is unknown, the
    /// operator is not one this reads, or its bound cannot be stated. A left
    /// shift is read for a constant count only.
    /// </summary>
    public static IntegerRange? ExactResult(
        BinaryOperatorKind kind,
        IntegerRange? left,
        IntegerRange? right
    )
    {
        if (left is not { } l || right is not { } r)
            return null;

        return kind switch
        {
            BinaryOperatorKind.Add => l.Plus(r),
            BinaryOperatorKind.Subtract => l.Minus(r),
            BinaryOperatorKind.Multiply => l.Times(r),
            BinaryOperatorKind.And => l.BitwiseAnd(r),
            BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr => l.BitwiseUnion(r),
            BinaryOperatorKind.LeftShift when r.Min == r.Max => l.ShiftedLeft(ConstantCountOf(r)),
            _ => null,
        };
    }

    /// <summary>A constant shift count as an <c>int</c>; <c>-1</c> (no valid count) when it does not fit one.</summary>
    private static int ConstantCountOf(IntegerRange count) =>
        count.Min >= 0 && count.Min <= int.MaxValue ? (int)count.Min : -1;

    private static Int128? ConstantOf(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: var value }
            ? value switch
            {
                sbyte v => v,
                byte v => v,
                short v => v,
                ushort v => v,
                int v => v,
                uint v => v,
                long v => v,
                ulong v => v,
                _ => null,
            }
            : null;
}
