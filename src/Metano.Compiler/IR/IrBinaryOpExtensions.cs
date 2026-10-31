namespace Metano.Compiler.IR;

/// <summary>Classification helpers for <see cref="IrBinaryOp"/>.</summary>
public static class IrBinaryOpExtensions
{
    /// <summary>
    /// Whether the operator assigns its left operand — simple (<c>=</c>) or
    /// compound (<c>+=</c>, <c>??=</c>, …).
    /// </summary>
    public static bool IsAssignment(this IrBinaryOp op) =>
        op
            is IrBinaryOp.Assign
                or IrBinaryOp.AddAssign
                or IrBinaryOp.SubtractAssign
                or IrBinaryOp.MultiplyAssign
                or IrBinaryOp.DivideAssign
                or IrBinaryOp.ModuloAssign
                or IrBinaryOp.BitwiseAndAssign
                or IrBinaryOp.BitwiseOrAssign
                or IrBinaryOp.BitwiseXorAssign
                or IrBinaryOp.LeftShiftAssign
                or IrBinaryOp.RightShiftAssign
                or IrBinaryOp.UnsignedRightShiftAssign
                or IrBinaryOp.NullCoalescingAssign;
}
