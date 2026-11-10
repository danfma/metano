using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The integer overflow reduction (spec 012 FR-021–FR-026, FR-075–FR-077):
/// the result of an operation that can leave the domain of its integer type
/// is wrapped in an <see cref="IrIntegerOverflow"/>, which
/// <see cref="IntegerOverflowLowering"/> decides from Roslyn. An increment
/// or a compound assignment stores the reduced value through the
/// read-modify-write lowering, so its receiver and index are evaluated once.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <paramref name="result"/> reduced as <paramref name="operation"/> needs, or unchanged.
    /// A constant operation that needs a reduction (<c>1u &lt;&lt; 31</c>,
    /// <c>~0u</c>) is the value Roslyn folded, which is already reduced.
    /// </summary>
    private static IrExpression ReduceOverflow(IOperation? operation, IrExpression result) =>
        IntegerOverflowLowering.ReductionOf(operation) is { } reduction
            ? FoldedOperatorConstant(operation) ?? reduction.Reduce(result)
            : result;

    /// <summary>The literal of a built-in operator Roslyn folded to a numeric constant, or <c>null</c>.</summary>
    private static IrExpression? FoldedOperatorConstant(IOperation? operation) =>
        operation
            is (IBinaryOperation or IUnaryOperation)
                and { ConstantValue: { HasValue: true, Value: { } value } }
        && IsNumericConstant(value)
            ? ConstantLiteralOfKind(value, NumericEndpoints.Of(operation.Type))
            : null;

    /// <summary>
    /// <c>x++</c>, <c>++x</c>, <c>x--</c> or <c>--x</c> on a value whose step
    /// is reduced (a 64-bit integer; an <c>sbyte</c>, <c>byte</c>,
    /// <c>short</c> or <c>ushort</c>; an <c>int</c> / <c>uint</c> in a checked
    /// context): <c>x = reduce(x ± 1)</c> (FR-024, FR-075, FR-077). A postfix
    /// form whose value is used yields the previous value as the stored one
    /// stepped back, <c>reduce((x = reduce(x + 1)) - 1)</c>: the step is
    /// invertible modulo the type's width, and a checked step that did not
    /// throw was exact, so no temporary
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
        if (IntegerOverflowLowering.ReductionOf(_semantic.GetOperation(node)) is not { } reduction)
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
}
