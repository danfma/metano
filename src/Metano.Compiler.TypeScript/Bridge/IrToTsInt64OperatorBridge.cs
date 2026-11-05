using System.Globalization;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renders the 64-bit division, remainder and shifts of spec 012
/// contracts/ts-emission.md § Operators (FR-027–FR-029) from the operand kind
/// the core stamps on the binary node. Division and remainder call the
/// <c>metano-runtime</c> helpers, which throw <c>DivideByZeroException</c> and
/// the <c>long.MinValue / -1</c> <c>OverflowException</c> as .NET does,
/// unless the divisor is a constant that cannot throw: native <c>bigint</c>
/// division already truncates toward zero and its remainder takes the
/// dividend's sign (DECIDED-8). A shift count, already masked by the core,
/// becomes a <c>bigint</c> (DECIDED-9). The staging <c>Number</c> mode keeps
/// its <c>intDiv</c> and native operators (DECIDED-22).
/// </summary>
internal static class IrToTsInt64OperatorBridge
{
    /// <summary>
    /// <paramref name="binary"/> rendered with <paramref name="map"/> mapping
    /// its operands, when it is a 64-bit division, remainder or shift in the
    /// <c>BigInt</c> mode; <c>null</c> otherwise.
    /// </summary>
    public static TsExpression? TryMap(
        IrBinaryExpression binary,
        Func<IrExpression, TsExpression> map
    )
    {
        // STAGING(012k): every 64-bit operator renders once the switch is gone.
        if (!Int64RepresentationScope.IsBigInt || binary.NumericKind is not { } kind)
            return null;

        if (!kind.IsSixtyFourBit())
            return null;

        var isSigned = kind.IsSigned();

        return binary.Operator switch
        {
            IrBinaryOp.IntegerDivide => Division(binary, isSigned, isRemainder: false, map),
            IrBinaryOp.Modulo => Division(binary, isSigned, isRemainder: true, map),
            IrBinaryOp.LeftShift => Shift(map(binary.Left), "<<", binary.Right, map),
            IrBinaryOp.RightShift => Shift(map(binary.Left), ">>", binary.Right, map),
            IrBinaryOp.UnsignedRightShift => Shift(
                UnsignedBits(isSigned, map(binary.Left)),
                ">>",
                binary.Right,
                map
            ),
            _ => null,
        };
    }

    private static TsExpression Division(
        IrBinaryExpression binary,
        bool isSigned,
        bool isRemainder,
        Func<IrExpression, TsExpression> map
    )
    {
        var dividend = map(binary.Left);
        var divisor = map(binary.Right);

        if (IsNonThrowingDivisor(binary.Right, isSigned))
            return new TsBinaryExpression(dividend, isRemainder ? "%" : "/", divisor);

        return RuntimeCall(Int64RuntimeHelpers.Division(isSigned, isRemainder), dividend, divisor);
    }

    /// <summary>
    /// A constant divisor other than <c>0</c>, and other than <c>-1</c> for a
    /// signed dividend, whose quotient and remainder cannot throw.
    /// </summary>
    private static bool IsNonThrowingDivisor(IrExpression divisor, bool isSigned) =>
        divisor is IrLiteral { Value: long or ulong or int } literal
        && Convert.ToDecimal(literal.Value, CultureInfo.InvariantCulture) switch
        {
            0m => false,
            -1m => !isSigned,
            _ => true,
        };

    /// <summary>
    /// <c>value op count</c> with the count as a <c>bigint</c>: a folded
    /// constant prints as a <c>bigint</c> literal, any other count is
    /// converted with <c>BigInt(…)</c>.
    /// </summary>
    private static TsBinaryExpression Shift(
        TsExpression value,
        string token,
        IrExpression count,
        Func<IrExpression, TsExpression> map
    ) => new(value, token, BigIntCount(count, map));

    private static TsExpression BigIntCount(
        IrExpression count,
        Func<IrExpression, TsExpression> map
    ) =>
        count is IrLiteral { Value: int constant }
            ? new TsLiteral(constant.ToString(CultureInfo.InvariantCulture) + "n")
            : BigInt(map(count));

    /// <summary>
    /// The bits <c>&gt;&gt;&gt;</c> shifts: a <c>long</c> as its unsigned
    /// 64-bit value (the core wraps the result back, FR-029); a
    /// <c>ulong</c> is already non-negative, so <c>&gt;&gt;</c> is logical.
    /// </summary>
    private static TsExpression UnsignedBits(bool isSigned, TsExpression value) =>
        isSigned ? AsIntN(isSigned: false, 64, value) : value;
}
