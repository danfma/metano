using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The 64-bit overflow reduction of the exact 64-bit lowering (spec 012
/// FR-021–FR-026): the result of an operation that can leave the 64-bit
/// domain is wrapped in an <see cref="IrIntegerOverflow"/>, which
/// <see cref="IntegerOverflowLowering"/> decides from Roslyn. An increment
/// or a compound assignment stores the reduced value through the
/// read-modify-write lowering, so its receiver and index are evaluated once.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The reduction <paramref name="operation"/> needs under the exact
    /// 64-bit lowering; <c>null</c> outside it, so the shipped <c>Number</c>
    /// mode keeps its IR (DECIDED-22).
    /// </summary>
    private IntegerOverflowReduction? OverflowReductionOf(IOperation? operation) =>
        _context.ExactInt64Lowering ? IntegerOverflowLowering.ReductionOf(operation) : null;

    /// <summary><paramref name="result"/> reduced as <paramref name="operation"/> needs, or unchanged.</summary>
    private IrExpression ReduceOverflow(IOperation? operation, IrExpression result) =>
        OverflowReductionOf(operation) is { } reduction ? reduction.Reduce(result) : result;

    /// <summary>
    /// <c>x++</c>, <c>++x</c>, <c>x--</c> or <c>--x</c> on a 64-bit value:
    /// <c>x = reduce(x ± 1n)</c> (FR-024). A postfix form whose value is used
    /// yields the previous value as the stored one stepped back,
    /// <c>reduce((x = reduce(x + 1n)) - 1n)</c>: the step is invertible modulo
    /// 2^64, and a checked step that did not throw was exact, so no temporary
    /// (and no immediately invoked function, NFR-004) holds the old value.
    /// </summary>
    private IrExpression? TryExtractReducedIncrement(
        ExpressionSyntax node,
        ExpressionSyntax operandSyntax,
        IrUnaryOp op,
        IrExpression target,
        bool isPrefix
    )
    {
        if (OverflowReductionOf(_semantic.GetOperation(node)) is not { } reduction)
            return null;

        var stepOperator = StepOperatorOf(op);
        var one = OneOf(operandSyntax);

        IrExpression Increment(IrExpression current) =>
            reduction.Reduce(new IrBinaryExpression(current, stepOperator, one));

        if (isPrefix || IsValueUnused(node))
            return BindTargetOnce(target, node, readTarget => WriteInPlace(readTarget, Increment));

        var inverseOperator = StepOperatorOf(InverseOf(op));

        return BindTargetOnce(
            target,
            node,
            readTarget =>
                PreviousValue(
                    reduction,
                    new IrBinaryExpression(
                        WriteInPlace(readTarget, Increment),
                        inverseOperator,
                        one
                    )
                )
        );
    }

    /// <summary>
    /// The stored value stepped back: wrapped again when the step wrapped,
    /// exact when it was checked.
    /// </summary>
    private static IrExpression PreviousValue(
        IntegerOverflowReduction reduction,
        IrExpression steppedBack
    ) => reduction.Mode is IrOverflowMode.Wrap ? reduction.Reduce(steppedBack) : steppedBack;

    private static IrUnaryOp InverseOf(IrUnaryOp op) =>
        op is IrUnaryOp.Increment ? IrUnaryOp.Decrement : IrUnaryOp.Increment;

    /// <summary>
    /// <c>target op= right</c> on a 64-bit value whose result can overflow:
    /// <c>target = reduce(target op right)</c> (FR-024).
    /// </summary>
    private IrExpression? TryExtractReducedCompoundAssignment(
        IrExpression target,
        AssignmentExpressionSyntax assign,
        IrExpression right
    )
    {
        if (
            MapCompoundAssignmentOperator(assign) is not { } op
            || OverflowReductionOf(_semantic.GetOperation(assign)) is not { } reduction
        )
            return null;

        return BuildReadModifyWrite(
            target,
            assign,
            readTarget => reduction.Reduce(new IrBinaryExpression(readTarget, op, right))
        );
    }

    /// <summary>
    /// The step of an extension property or indexer increment: the 64-bit
    /// <c>1n</c> under the exact lowering, the plain <c>1</c> the shipped
    /// <c>Number</c> mode has always emitted (DECIDED-22).
    /// </summary>
    private IrExpression ExtensionIncrementStepOf(ExpressionSyntax operand) =>
        _context.ExactInt64Lowering ? OneOf(operand) : OneLiteral;
}
