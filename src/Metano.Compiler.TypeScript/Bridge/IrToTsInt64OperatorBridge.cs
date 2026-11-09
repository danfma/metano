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
/// unless the core says the constant divisor cannot throw: native
/// <c>bigint</c> division already truncates toward zero and its remainder
/// takes the dividend's sign (DECIDED-8). A shift count, already masked by
/// the core, becomes a <c>bigint</c> (DECIDED-9).
/// </summary>
internal static class IrToTsInt64OperatorBridge
{
    /// <summary>
    /// <paramref name="binary"/> rendered with <paramref name="map"/> mapping
    /// its operands, when it is a 64-bit division, remainder or shift;
    /// <c>null</c> otherwise.
    /// </summary>
    public static TsExpression? TryMap(
        IrBinaryExpression binary,
        Func<IrExpression, TsExpression> map
    )
    {
        if (SixtyFourBitKindOf(binary) is not { } kind)
            return null;

        var isSigned = kind.IsSigned();

        return binary.Operator switch
        {
            IrBinaryOp.IntegerDivide => Division(
                binary,
                "/",
                Int64RuntimeHelpers.Quotient(isSigned),
                map
            ),
            IrBinaryOp.Modulo => Division(
                binary,
                "%",
                Int64RuntimeHelpers.Remainder(isSigned),
                map
            ),
            IrBinaryOp.LeftShift => Shift(map(binary.Left), "<<", binary.Right, map),
            IrBinaryOp.RightShift => Shift(map(binary.Left), ">>", binary.Right, map),
            IrBinaryOp.UnsignedRightShift => UnsignedRightShift(binary, isSigned, map),
            _ => null,
        };
    }

    private static IrNumericKind? SixtyFourBitKindOf(IrBinaryExpression binary) =>
        binary.NumericKind is { } kind && kind.IsSixtyFourBit() ? kind : null;

    private static TsExpression Division(
        IrBinaryExpression binary,
        string nativeOperator,
        string runtimeHelper,
        Func<IrExpression, TsExpression> map
    )
    {
        var dividend = map(binary.Left);
        var divisor = map(binary.Right);

        return binary.HasNonThrowingConstantDivisor()
            ? new TsBinaryExpression(dividend, nativeOperator, divisor)
            : RuntimeCall(runtimeHelper, dividend, divisor);
    }

    /// <summary>
    /// <c>&gt;&gt;&gt;</c>: on a <c>ulong</c>, <c>&gt;&gt;</c>, already
    /// logical for a non-negative <c>bigint</c>; on a <c>long</c>, the
    /// unsigned bits shifted and read back as signed,
    /// <c>BigInt.asIntN(64, BigInt.asUintN(64, l) &gt;&gt; c)</c> (FR-029),
    /// since a count of <c>0</c> keeps a value above <c>long.MaxValue</c>.
    /// </summary>
    private static TsExpression UnsignedRightShift(
        IrBinaryExpression binary,
        bool isSigned,
        Func<IrExpression, TsExpression> map
    )
    {
        if (!isSigned)
            return Shift(map(binary.Left), ">>", binary.Right, map);

        var unsignedBits = AsIntN(isSigned: false, 64, map(binary.Left));

        return AsIntN(isSigned: true, 64, Shift(unsignedBits, ">>", binary.Right, map));
    }

    /// <summary>
    /// <c>value op count</c> over the already mapped <paramref name="value"/>
    /// and the IR <paramref name="count"/>, which stays unmapped so a folded
    /// constant prints as a <c>bigint</c> literal; any other count is
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
}
