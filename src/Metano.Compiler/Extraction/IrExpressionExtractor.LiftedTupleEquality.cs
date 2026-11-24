using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The lifted tuple <c>==</c> / <c>!=</c> (#415): a <c>(int, int)?</c>
/// operand compares as C# does. Both operands are evaluated first (their
/// non-literal leaves are bound in source order when one has a side effect);
/// then a missing nullable tuple equals only another missing one, and a
/// present one compares element by element like the non-lifted operator.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrExpression LiftedTupleEquality(TupleOperand left, TupleOperand right, bool negated)
    {
        var bindings = new List<(string Name, IrExpression Value)>();
        var isSideEffectFree = left.AllLeavesSideEffectFree() && right.AllLeavesSideEffectFree();
        var boundLeft = isSideEffectFree ? left : BindLeaves(left, bindings);
        var boundRight = isSideEffectFree ? right : BindLeaves(right, bindings);

        var comparison = CompareLiftedOperands(
            boundLeft,
            boundRight,
            MissingTestOf(boundLeft),
            MissingTestOf(boundRight),
            negated
        );

        return WrapInLetBindings(comparison, bindings);
    }

    /// <summary>
    /// The comparison once both operands are evaluated: one nullable side
    /// (<c>t != null &amp;&amp; …</c>), or two, which are equal when both are
    /// missing and unequal when only one is.
    /// </summary>
    private IrExpression CompareLiftedOperands(
        TupleOperand left,
        TupleOperand right,
        IrExpression? leftMissing,
        IrExpression? rightMissing,
        bool negated
    )
    {
        var present = CompareTupleOperands(Unwrapped(left), Unwrapped(right), negated);

        if (leftMissing is null || rightMissing is null)
        {
            var missing = (leftMissing ?? rightMissing)!;

            return negated
                ? new IrBinaryExpression(missing, IrBinaryOp.LogicalOr, present)
                : new IrBinaryExpression(Not(missing), IrBinaryOp.LogicalAnd, present);
        }

        var eitherMissing = new IrBinaryExpression(leftMissing, IrBinaryOp.LogicalOr, rightMissing);
        var bothMissing = new IrBinaryExpression(leftMissing, IrBinaryOp.LogicalAnd, rightMissing);

        return new IrConditionalExpression(
            eitherMissing,
            negated ? Not(bothMissing) : bothMissing,
            present
        );
    }

    /// <summary>The <c>== null</c> test of a nullable tuple operand; <c>null</c> for any other.</summary>
    private static IrExpression? MissingTestOf(TupleOperand operand) =>
        operand is ValueOperand { Type: { } type } value && type.IsNullableValueType()
            ? new IrBinaryExpression(value.Value, IrBinaryOp.Equal, NullLiteral)
            : null;

    /// <summary>A nullable tuple operand read as its present tuple; any other as it is.</summary>
    private static TupleOperand Unwrapped(TupleOperand operand) =>
        operand is ValueOperand { Type: { } type } value && type.IsNullableValueType()
            ? value with
            {
                Type = type.UnwrapNullableValueType(),
            }
            : operand;

    private static IrUnaryExpression Not(IrExpression operand) =>
        new(IrUnaryOp.LogicalNot, operand);
}
