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
/// <see cref="NarrowIntegerTsExpressions"/> (FR-075–FR-077).
/// </summary>
internal static class IrToTsIntegerOverflowBridge
{
    public static TsExpression Map(IrIntegerOverflow overflow, TsExpression value)
    {
        var isChecked = overflow.Mode switch
        {
            IrOverflowMode.Wrap => false,
            IrOverflowMode.Check => true,
            _ => throw new ArgumentOutOfRangeException(
                nameof(overflow),
                overflow.Mode,
                $"Unknown overflow mode '{overflow.Mode}'."
            ),
        };

        if (!overflow.Kind.IsSixtyFourBit())
            return NarrowIntegerTsExpressions.Reduced(overflow.Kind, isChecked, value);

        var isSigned = overflow.Kind.IsSigned();

        return isChecked
            ? RuntimeCall(Int64RuntimeHelpers.CheckedRange(isSigned), value)
            : AsIntN(isSigned, 64, value);
    }
}
