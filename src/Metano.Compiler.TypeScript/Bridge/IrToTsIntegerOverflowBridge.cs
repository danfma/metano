using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renders an <see cref="IrIntegerOverflow"/>. A 64-bit result follows spec
/// 012 contracts/ts-emission.md § Overflow: an unchecked one wraps with the
/// native <c>BigInt.asIntN(64, …)</c> / <c>BigInt.asUintN(64, …)</c>, which
/// imports nothing, and a checked one calls <c>checkedInt64</c> /
/// <c>checkedUInt64</c>, which throw <c>OverflowException</c> (DECIDED-5).
/// A result of 32 bits or fewer is a <c>number</c>, reduced by
/// <see cref="NarrowIntegerTsExpressions"/> (FR-075–FR-077). The wrap of an
/// <c>int</c> / <c>uint</c> product is <c>Math.imul</c> (spec 001 FR-074),
/// and the wrap of an <c>int</c> left shift is the shift, which JavaScript
/// already computes in 32 signed bits.
/// </summary>
internal static class IrToTsIntegerOverflowBridge
{
    private const string Multiply32 = "Math.imul";

    public static TsExpression Map(IrIntegerOverflow overflow, Func<IrExpression, TsExpression> map)
    {
        if (IsThirtyTwoBitWrap(overflow))
            return ThirtyTwoBitWrap(overflow, map);

        var value = map(overflow.Value);

        if (!overflow.Kind.IsSixtyFourBit())
            return NarrowIntegerTsExpressions.Reduced(overflow.Kind, overflow.Mode, value);

        var isSigned = overflow.Kind.IsSigned();

        return overflow.Mode switch
        {
            IrOverflowMode.Wrap => AsIntN(isSigned, 64, value),
            IrOverflowMode.Check => RuntimeCall(Int64RuntimeHelpers.CheckedRange(isSigned), value),
            _ => throw new ArgumentOutOfRangeException(
                nameof(overflow),
                overflow.Mode,
                $"Unknown overflow mode '{overflow.Mode}'."
            ),
        };
    }

    /// <summary>
    /// <paramref name="operand"/> as an operand whose JavaScript consumer
    /// reduces it modulo 2^32 itself (spec 001 FR-076): an <c>int</c> /
    /// <c>uint</c> wrap loses its trailing <c>| 0</c> / <c>&gt;&gt;&gt; 0</c>,
    /// recursively through the operands of <c>+</c>, <c>-</c> and unary
    /// <c>-</c>; a product keeps <c>Math.imul</c> (a <c>uint</c> one drops
    /// only its <c>&gt;&gt;&gt; 0</c>, becoming the <c>int</c> wrap). The
    /// unreduced value stays exact: a sum of a few 32-bit values is far
    /// inside 2^53.
    /// </summary>
    public static IrExpression ReducedByConsumer(IrExpression operand) =>
        operand switch
        {
            IrIntegerOverflow { Value: IrBinaryExpression { Operator: IrBinaryOp.Multiply } } wrap
                when IsThirtyTwoBitWrap(wrap) => wrap with
            {
                Kind = IrNumericKind.Int32,
            },
            IrIntegerOverflow wrap when IsThirtyTwoBitWrap(wrap) => WithOperandsReducedByConsumer(
                wrap.Value
            ),
            _ => operand,
        };

    /// <summary>
    /// <paramref name="expression"/> with its operands passed through
    /// <see cref="ReducedByConsumer"/> when it
    /// <see cref="ReducesOperandsModulo2To32">reduces them itself</see>
    /// (FR-076); unchanged otherwise.
    /// </summary>
    public static IrExpression WithOperandsItReduces(IrExpression expression) =>
        expression switch
        {
            _ when !ReducesOperandsModulo2To32(expression) => expression,
            IrBinaryExpression binary => binary with
            {
                Left = ReducedByConsumer(binary.Left),
                Right = ReducedByConsumer(binary.Right),
            },
            IrUnaryExpression unary => unary with { Operand = ReducedByConsumer(unary.Operand) },
            IrNumericConversion conversion => conversion with
            {
                Operand = ReducedByConsumer(conversion.Operand),
            },
            _ => expression,
        };

    /// <summary>
    /// Whether the JavaScript form of <paramref name="consumer"/> applies
    /// <c>ToInt32</c> / <c>ToUint32</c> to its operands: <c>&amp;</c>,
    /// <c>|</c>, <c>^</c> and the shifts of a 32-bit or narrower integer,
    /// <c>~</c>, and an unchecked, non-lifted narrowing between integers of
    /// 32 bits or fewer (a checked one reads the exact value). The one rule
    /// of FR-076's consumers besides the wraps and <c>Math.imul</c>.
    /// </summary>
    private static bool ReducesOperandsModulo2To32(IrExpression consumer) =>
        consumer switch
        {
            IrBinaryExpression binary => IsThirtyTwoBitBitwise(binary),
            IrUnaryExpression { Operator: IrUnaryOp.BitwiseNot } => true,
            IrNumericConversion { IsChecked: false, IsLifted: false, NarrowsInteger: true } => true,
            _ => false,
        };

    /// <summary>
    /// <c>| 0</c> / <c>&gt;&gt;&gt; 0</c> over the wrapped operation, whose
    /// own operands the wrap reduces (FR-076); <c>Math.imul</c> for a
    /// product, whose <c>int</c> result needs nothing more.
    /// </summary>
    private static TsExpression ThirtyTwoBitWrap(
        IrIntegerOverflow overflow,
        Func<IrExpression, TsExpression> map
    )
    {
        if (overflow.Value is IrBinaryExpression { Operator: IrBinaryOp.Multiply } product)
        {
            var imul = new TsCallExpression(
                new TsIdentifier(Multiply32),
                [map(ReducedByConsumer(product.Left)), map(ReducedByConsumer(product.Right))]
            );

            return overflow.Kind is IrNumericKind.Int32
                ? imul
                : NarrowIntegerTsExpressions.Wrapped(overflow.Kind, imul);
        }

        var value = map(WithOperandsReducedByConsumer(overflow.Value));

        return IsNativeInt32Wrap(overflow)
            ? value
            : NarrowIntegerTsExpressions.Wrapped(overflow.Kind, value);
    }

    /// <summary>The operands of a wrapped <c>+</c>, <c>-</c> or unary <c>-</c> passed through <see cref="ReducedByConsumer"/>.</summary>
    private static IrExpression WithOperandsReducedByConsumer(IrExpression value) =>
        value switch
        {
            IrBinaryExpression { Operator: IrBinaryOp.Add or IrBinaryOp.Subtract } sum => sum with
            {
                Left = ReducedByConsumer(sum.Left),
                Right = ReducedByConsumer(sum.Right),
            },
            IrUnaryExpression { Operator: IrUnaryOp.Negate } negation => negation with
            {
                Operand = ReducedByConsumer(negation.Operand),
            },
            _ => value,
        };

    private static bool IsThirtyTwoBitWrap(IrIntegerOverflow overflow) =>
        overflow
            is { Mode: IrOverflowMode.Wrap, Kind: IrNumericKind.Int32 or IrNumericKind.UInt32 };

    private static bool IsThirtyTwoBitBitwise(IrBinaryExpression binary) =>
        binary
            is {
                Operator: IrBinaryOp.BitwiseAnd
                    or IrBinaryOp.BitwiseOr
                    or IrBinaryOp.BitwiseXor
                    or IrBinaryOp.LeftShift
                    or IrBinaryOp.RightShift
                    or IrBinaryOp.UnsignedRightShift,
                NumericKind: { } kind,
            }
        && kind.IsThirtyTwoBitOrNarrower();

    /// <summary>
    /// Whether <paramref name="overflow"/> wraps an <c>int</c> left shift:
    /// JavaScript's <c>&lt;&lt;</c> computes in 32 signed bits, which is that
    /// wrap already, so the shift is emitted as it is (FR-076).
    /// </summary>
    private static bool IsNativeInt32Wrap(IrIntegerOverflow overflow) =>
        overflow
            is {
                Kind: IrNumericKind.Int32,
                Mode: IrOverflowMode.Wrap,
                Value: IrBinaryExpression
                {
                    Operator: IrBinaryOp.LeftShift,
                    NumericKind: IrNumericKind.Int32
                }
            };
}
