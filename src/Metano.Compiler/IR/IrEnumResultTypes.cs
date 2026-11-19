namespace Metano.Compiler.IR;

/// <summary>
/// The enum result type a built-in enum operator carries
/// (<see cref="IrBinaryExpression.EnumResultType"/>,
/// <see cref="IrUnaryExpression.EnumResultType"/>), read and written through
/// the overflow reduction of a 64-bit enum's operator, whose value the
/// operator still is (#701), and through the read-modify-write store of such
/// a reduced compound assignment.
/// </summary>
public static class IrEnumResultTypes
{
    /// <summary>The enum result type of <paramref name="value"/>'s operator; <c>null</c> for none.</summary>
    public static IrTypeRef? Of(IrExpression value) =>
        value switch
        {
            IrBinaryExpression { Operator: not IrBinaryOp.Assign } binary => binary.EnumResultType,
            IrUnaryExpression unary => unary.EnumResultType,
            IrIntegerOverflow overflow => Of(overflow.Value),
            _ => null,
        };

    /// <summary>
    /// <paramref name="value"/> with <paramref name="enumType"/> as its
    /// operator's enum result type (<c>null</c> clears it).
    /// </summary>
    public static IrExpression With(IrExpression value, IrTypeRef? enumType) =>
        value switch
        {
            IrBinaryExpression
            {
                Operator: IrBinaryOp.Assign,
                Right: IrIntegerOverflow stored,
            } store => store with
            {
                Right = With(stored, enumType),
            },
            IrBinaryExpression { Operator: not IrBinaryOp.Assign } binary => binary with
            {
                EnumResultType = enumType,
            },
            IrUnaryExpression unary => unary with { EnumResultType = enumType },
            IrIntegerOverflow overflow => overflow with { Value = With(overflow.Value, enumType) },
            _ => value,
        };
}
