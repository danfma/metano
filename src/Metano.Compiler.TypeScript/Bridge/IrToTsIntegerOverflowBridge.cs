using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renders an <see cref="IrIntegerOverflow"/> per spec 012
/// contracts/ts-emission.md § Overflow: an unchecked result wraps with the
/// native <c>BigInt.asIntN(64, …)</c> / <c>BigInt.asUintN(64, …)</c>, which
/// imports nothing, and a checked one calls <c>checkedInt64</c> /
/// <c>checkedUInt64</c>, which throw <c>OverflowException</c> (DECIDED-5).
/// </summary>
internal static class IrToTsIntegerOverflowBridge
{
    public static TsExpression Map(IrIntegerOverflow overflow, TsExpression value)
    {
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
}
