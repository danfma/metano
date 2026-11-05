using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Compound assignments and increments with no native target operator,
/// lowered to <c>target = compute(target)</c> with the target's receiver and
/// index bound once: integer and boolean compound operators, lifted
/// compound operators, and nullable <c>++</c> / <c>--</c>.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <paramref name="op"/> narrowed to its integer-division or boolean-logic
    /// form when <paramref name="operation"/> binds to one; unchanged
    /// otherwise.
    /// </summary>
    private IrBinaryOp NarrowBuiltinOperator(IrBinaryOp op, ExpressionSyntax operation) =>
        NarrowToBooleanLogic(NarrowToIntegerDivision(op, operation), operation);

    /// <summary>
    /// Builds <paramref name="target"/> <c>op=</c> <c>right</c>. An integer
    /// <c>/=</c>, a boolean <c>&amp;=</c> / <c>|=</c> / <c>^=</c> and, under
    /// the exact 64-bit lowering, a 64-bit compound operator that wraps,
    /// checks, divides or shifts have no compound operator in the targets
    /// that keeps C# semantics, so each becomes the read-modify-write
    /// <c>target = target op right</c> (see <see cref="BuildReadModifyWrite"/>)
    /// storing the value <see cref="CompoundValue"/> computes.
    /// </summary>
    private IrExpression BuildCompoundAssignment(
        IrExpression target,
        AssignmentExpressionSyntax assign
    )
    {
        var right = Extract(assign.Right);

        if (MapCompoundAssignmentOperator(assign) is { } op && RequiresReadModifyWrite(op, assign))
            return BuildReadModifyWrite(
                target,
                assign,
                readTarget => CompoundValue(assign, readTarget, op, right)
            );

        return new IrBinaryExpression(target, MapAssignmentOp(assign.Kind()), right);
    }

    private bool RequiresReadModifyWrite(IrBinaryOp op, AssignmentExpressionSyntax assign) =>
        HasNoTargetCompoundOperator(op)
        || NeedsOverflowReduction(assign)
        || NeedsSixtyFourBitReadModifyWrite(op, assign);

    private static bool HasNoTargetCompoundOperator(IrBinaryOp op) =>
        op
            is IrBinaryOp.IntegerDivide
                or IrBinaryOp.BooleanAnd
                or IrBinaryOp.BooleanOr
                or IrBinaryOp.BooleanXor;

    private bool NeedsOverflowReduction(AssignmentExpressionSyntax assign) =>
        OverflowReductionOf(_semantic.GetOperation(assign)) is not null;

    /// <summary>
    /// The value <c>target op= right</c> stores: the binary form over the
    /// current value, reduced as the compound operation needs (FR-024).
    /// </summary>
    private IrExpression CompoundValue(
        AssignmentExpressionSyntax assign,
        IrExpression current,
        IrBinaryOp op,
        IrExpression right
    ) =>
        ReduceOverflow(
            _semantic.GetOperation(assign),
            BuildNumericBinary(
                assign,
                op,
                current,
                new OperatorOperand(right, MayBeNull: false, assign.Right)
            )
        );

    /// <summary>
    /// <c>target = compute(target)</c>, reading the target twice (see
    /// <see cref="BindTargetOnce"/>).
    /// </summary>
    private IrExpression BuildReadModifyWrite(
        IrExpression target,
        ExpressionSyntax operation,
        Func<IrExpression, IrExpression> compute
    ) => BindTargetOnce(target, operation, readTarget => WriteInPlace(readTarget, compute));

    /// <summary>
    /// Builds an expression that reads and writes <paramref name="target"/>
    /// through the reusable form <paramref name="build"/> receives. The
    /// receiver of a member access and the receiver and index of an element
    /// access are evaluated once, in source order, as C# does for a compound
    /// assignment or an increment: a reusable one is repeated, anything else
    /// is let-bound first. Any other assignable shape is reported as
    /// unsupported rather than evaluated twice, and so is a dictionary
    /// entry: the read of a missing key would yield <c>undefined</c> where C#
    /// throws <c>KeyNotFoundException</c>.
    /// </summary>
    private IrExpression BindTargetOnce(
        IrExpression target,
        ExpressionSyntax operation,
        Func<IrExpression, IrExpression> build
    ) =>
        target switch
        {
            IrElementAccess { TargetType: IrMapTypeRef } => ReportUnsupported(
                operation,
                operation.Kind().ToString(),
                $"'{operation}' on a dictionary entry is not supported."
            ),
            _ when IrEvaluationTraits.IsReusableReceiver(target) => build(target),
            IrMemberAccess member => BindReceiverOnce(
                member.Target,
                receiver => build(member with { Target = receiver })
            ),
            IrElementAccess element => BindElementOnce(element, build),
            _ => ReportUnsupported(
                operation,
                operation.Kind().ToString(),
                $"'{operation}' is not supported: its target cannot be read and written "
                    + "without evaluating it twice."
            ),
        };

    private IrExpression BindElementOnce(
        IrElementAccess element,
        Func<IrExpression, IrExpression> build
    ) =>
        BindReceiverOnce(
            element.Target,
            receiver =>
                BindValueOnce(
                    element.Index,
                    index => build(element with { Target = receiver, Index = index })
                )
        );

    private static IrBinaryExpression WriteInPlace(
        IrExpression target,
        Func<IrExpression, IrExpression> compute
    ) => new(target, IrBinaryOp.Assign, compute(target));
}
