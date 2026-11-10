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
/// <see cref="NarrowIntegerTsExpressions"/> (FR-075–FR-077), except the
/// wrap of an <c>int</c> left shift, which JavaScript already computes.
/// </summary>
internal static class IrToTsIntegerOverflowBridge
{
    public static TsExpression Map(IrIntegerOverflow overflow, TsExpression value)
    {
        if (IsNativeInt32Wrap(overflow))
            return value;

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
    /// Whether <paramref name="overflow"/> wraps an <c>int</c> left shift:
    /// JavaScript's <c>&lt;&lt;</c> computes in 32 signed bits, which is that
    /// wrap already, so the shift is emitted as it is (FR-076).
    /// </summary>
    public static bool IsNativeInt32Wrap(IrIntegerOverflow overflow) =>
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
