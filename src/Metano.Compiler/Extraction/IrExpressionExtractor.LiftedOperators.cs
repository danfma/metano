using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// C# lifts operators over nullable value types: an arithmetic, bitwise or
/// shift operator yields <c>null</c> when an operand is <c>null</c>, and a
/// relational operator yields <c>false</c>. JavaScript does neither
/// (<c>null + 1</c> is <c>1</c>, <c>null &lt; 1</c> is <c>true</c>) and Dart
/// rejects a nullable operand, so these operators are lowered here, over the
/// ordinary IR, to an explicit null check. Each side-effecting operand is
/// evaluated exactly once, in source order; a pure operand moved into a
/// null-conditional guard is skipped when the receiver is null. Equality operators keep their ordinary lowering, as
/// they already compare <c>null</c> as a value; the three-valued
/// <c>bool?</c> logical operators (<c>&amp;</c>, <c>|</c>, <c>^</c>) and
/// <c>++</c> / <c>--</c> are not lowered here either.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private static readonly IrLiteral NullLiteral = new(null, IrLiteralKind.Null);
    private static readonly IrLiteral FalseLiteral = new(false, IrLiteralKind.Boolean);

    /// <summary>
    /// One operand of an operator: the extracted value, whether it can be
    /// <c>null</c> as a nullable value, and its source, for type lookups.
    /// </summary>
    private readonly record struct OperatorOperand(
        IrExpression Value,
        bool MayBeNull,
        ExpressionSyntax Syntax
    )
    {
        public OperatorOperand WithValue(IrExpression value) => this with { Value = value };
    }

    /// <summary>
    /// The lifted operator: the ordinary operator over the non-null
    /// operands, and whether it is relational (yielding <c>false</c> rather
    /// than <c>null</c> for a <c>null</c> operand).
    /// </summary>
    private sealed record LiftedOperator(
        bool IsRelational,
        Func<OperatorOperand, OperatorOperand, IrExpression> Apply
    );

    /// <summary>
    /// <c>left op right</c> when <c>op</c> is a lifted operator (predefined,
    /// or user-defined like <c>DateOnly</c>'s comparisons), or <c>null</c>
    /// when the ordinary lowering applies.
    /// </summary>
    private IrExpression? TryExtractLiftedBinary(BinaryExpressionSyntax bin, IrBinaryOp op)
    {
        if (!PropagatesNullThroughLift(_semantic.GetOperation(bin), op))
            return null;

        return LiftBinary(OperandOf(bin.Left), OperandOf(bin.Right), LiftedOperatorFor(op));
    }

    /// <summary>
    /// <c>op operand</c> when <c>op</c> is a lifted unary operator
    /// (<c>-x</c>, <c>~x</c>, <c>!x</c> over a nullable value), or <c>null</c>
    /// when the ordinary lowering applies.
    /// </summary>
    private IrExpression? TryExtractLiftedUnary(
        ExpressionSyntax node,
        IrUnaryOp op,
        ExpressionSyntax operandSyntax
    )
    {
        if (_semantic.GetOperation(node) is not IUnaryOperation { IsLifted: true })
            return null;

        return LiftUnary(
            OperandOf(operandSyntax),
            operand => BuildUnary(op, operand, operandSyntax)
        );
    }

    /// <summary>
    /// <c>target op= value</c> when the compound operator is lifted:
    /// <c>target = target op value</c> with the lifted operator. Only for a
    /// target that can be read twice without side effects; any other target
    /// keeps the ordinary compound lowering.
    /// </summary>
    private IrExpression? TryExtractLiftedCompoundAssignment(
        AssignmentExpressionSyntax assign,
        IrExpression target
    )
    {
        if (TryMapCompoundAssignmentToBinary(assign.Kind()) is not { } mappedOp)
            return null;

        var op = NarrowToIntegerDivision(mappedOp, assign);

        if (
            !PropagatesNullThroughLift(_semantic.GetOperation(assign), op)
            || !IrEvaluationTraits.IsReusableReceiver(target)
        )
            return null;

        var lifted = LiftBinary(
            new OperatorOperand(target, IsNullableValueOperand(assign.Left), assign.Left),
            OperandOf(assign.Right),
            LiftedOperatorFor(op)
        );

        return new IrBinaryExpression(target, IrBinaryOp.Assign, lifted);
    }

    private LiftedOperator LiftedOperatorFor(IrBinaryOp op) =>
        new(op.IsRelational(), (left, right) => BuildBinary(op, left, right, isLifted: true));

    /// <summary>
    /// Whether <paramref name="operation"/> is a lifted operator that yields
    /// <c>null</c> (or, for a relational operator, <c>false</c>) for a
    /// <c>null</c> operand. Equality compares <c>null</c> as a value, and
    /// <c>bool?</c> <c>&amp;</c> / <c>|</c> / <c>^</c> follow three-valued
    /// logic (<c>null &amp; false</c> is <c>false</c>); both keep the
    /// ordinary lowering.
    /// </summary>
    private static bool PropagatesNullThroughLift(IOperation? operation, IrBinaryOp op)
    {
        var isLifted =
            operation
            is IBinaryOperation { IsLifted: true }
                or ICompoundAssignmentOperation { IsLifted: true };

        if (!isLifted || op.IsEquality())
            return false;

        var isThreeValuedLogic =
            op.IsBitwise()
            && operation?.Type.UnderlyingValueType()?.SpecialType == SpecialType.System_Boolean;

        return !isThreeValuedLogic;
    }

    private OperatorOperand OperandOf(ExpressionSyntax syntax) =>
        new(Extract(syntax), IsNullableValueOperand(syntax), syntax);

    /// <summary>
    /// Whether an operand of a lifted operator can be <c>null</c>: its own
    /// type is a nullable value type, or it is a <c>null</c> /
    /// <c>default</c> literal converted to one. A non-nullable operand is
    /// converted implicitly and needs no test.
    /// </summary>
    private bool IsNullableValueOperand(ExpressionSyntax operand)
    {
        var info = _semantic.GetTypeInfo(operand);

        return (info.Type ?? info.ConvertedType).IsNullableValueType();
    }

    private IrExpression LiftBinary(OperatorOperand left, OperatorOperand right, LiftedOperator op)
    {
        if (GuardThroughConditionalAccess(left, right, op) is not { } guarded)
            return BindAndGuardOperands(left, right, op);

        // The guarded access yields `null` when a receiver is null; a
        // relational operator yields `false` there instead.
        return op.IsRelational
            ? new IrBinaryExpression(guarded, IrBinaryOp.NullCoalescing, FalseLiteral)
            : guarded;
    }

    /// <summary>
    /// An operand that is a null-conditional access (<c>o?.Items.Count</c>)
    /// is null exactly when its receiver chain is, so the operator moves
    /// into the access: <c>o?.Items.Count + 1</c> lowers to the access
    /// <c>o?.(Items.Count + 1)</c>, which a backend renders as a guard on
    /// <c>o</c> instead of binding the whole operand. The other operand
    /// moves too, so it must have no side effects (getters and division are
    /// assumed pure, as in <see cref="IrEvaluationTraits"/>, so a throwing
    /// one is skipped when the receiver is null) and read no enclosing
    /// receiver. When the access is the right operand, the left one is read
    /// after the access's receiver, so that receiver must be side-effect
    /// free too. Returns <c>null</c> when the shape does not apply.
    /// </summary>
    private IrNullConditionalAccess? GuardThroughConditionalAccess(
        OperatorOperand left,
        OperatorOperand right,
        LiftedOperator op
    )
    {
        if (
            ConditionalAccessOf(left) is var (leftAccess, leftContinuation)
            && CanMoveIntoGuard(right)
        )
            return leftAccess with
            {
                WhenNotNull = GuardOrLiftOperands(leftContinuation, right, op),
            };

        if (
            ConditionalAccessOf(right) is var (rightAccess, rightContinuation)
            && CanMoveIntoGuardAfter(left, rightAccess)
        )
            return rightAccess with
            {
                WhenNotNull = GuardOrLiftOperands(left, rightContinuation, op),
            };

        return null;
    }

    private IrExpression GuardOrLiftOperands(
        OperatorOperand left,
        OperatorOperand right,
        LiftedOperator op
    ) => GuardThroughConditionalAccess(left, right, op) ?? BindAndGuardOperands(left, right, op);

    private static bool CanMoveIntoGuard(OperatorOperand operand) =>
        IrEvaluationTraits.IsSideEffectFree(operand.Value)
        && IrEvaluationTraits.CountConditionalReceiverReads(operand.Value) == 0;

    /// <summary>
    /// A left operand moved into the guard of a right-operand access is read
    /// after that access's receiver, so the receiver must not change it.
    /// </summary>
    private static bool CanMoveIntoGuardAfter(
        OperatorOperand left,
        IrNullConditionalAccess rightAccess
    ) => CanMoveIntoGuard(left) && CanBeReadAfter(left, rightAccess.Receiver);

    /// <summary>
    /// Whether <paramref name="earlier"/> can be read after
    /// <paramref name="laterEvaluated"/> runs and still see the value C#
    /// reads first: a literal always can, anything else only when
    /// <paramref name="laterEvaluated"/> has no side effects to change it.
    /// </summary>
    private static bool CanBeReadAfter(OperatorOperand earlier, IrExpression laterEvaluated) =>
        earlier.Value is IrLiteral || IrEvaluationTraits.IsSideEffectFree(laterEvaluated);

    /// <summary>
    /// The access an operand extracted to and the operand its
    /// <see cref="IrNullConditionalAccess.WhenNotNull"/> stands for, typed by
    /// the last continuation of the source access (<c>a?.b?.c</c> nests its
    /// receivers, so the outermost IR access ends in <c>.c</c>).
    /// </summary>
    private (IrNullConditionalAccess Access, OperatorOperand Continuation)? ConditionalAccessOf(
        OperatorOperand operand
    )
    {
        if (
            !operand.MayBeNull
            || operand.Value is not IrNullConditionalAccess access
            || Unparenthesized(operand.Syntax) is not ConditionalAccessExpressionSyntax syntax
        )
            return null;

        var continuation = LastContinuation(syntax);

        return (
            access,
            new OperatorOperand(
                access.WhenNotNull,
                IsNullableValueOperand(continuation),
                continuation
            )
        );
    }

    /// <summary>
    /// Binds the operands once, in source order — the left operand too when
    /// the right one has side effects that could change what it reads — and
    /// guards the operator: <c>l == null || r == null ? null : l op r</c>, or
    /// <c>l != null &amp;&amp; r != null &amp;&amp; l op r</c> for a relational
    /// operator. Only the operands that can be <c>null</c> are tested.
    /// </summary>
    private IrExpression BindAndGuardOperands(
        OperatorOperand left,
        OperatorOperand right,
        LiftedOperator op
    )
    {
        if (!left.MayBeNull && !right.MayBeNull)
            return op.Apply(left, right);

        // `x + null` is always null (`x < null` always false); the other
        // operand is still evaluated.
        if (left.Value is IrLiteral { Kind: IrLiteralKind.Null })
            return BindValueOnce(right.Value, _ => NullResultOf(op));

        if (right.Value is IrLiteral { Kind: IrLiteralKind.Null })
            return BindValueOnce(left.Value, _ => NullResultOf(op));

        var leftIsReusable =
            IrEvaluationTraits.IsSideEffectFree(left.Value) && CanBeReadAfter(left, right.Value);

        return BindOnce(
            left.Value,
            leftIsReusable,
            boundLeft =>
                BindValueOnce(
                    right.Value,
                    boundRight =>
                        GuardLiftedOperator(
                            left.WithValue(boundLeft),
                            right.WithValue(boundRight),
                            op
                        )
                )
        );
    }

    private static IrExpression GuardLiftedOperator(
        OperatorOperand left,
        OperatorOperand right,
        LiftedOperator op
    )
    {
        var applied = op.Apply(left, right);

        var maybeNullValues = new[] { left, right }
            .Where(operand => operand.MayBeNull)
            .Select(operand => operand.Value)
            .ToList();

        if (op.IsRelational)
            return maybeNullValues
                .Select(IsNotNull)
                .Append(applied)
                .Aggregate(Combining(IrBinaryOp.LogicalAnd));

        var anyNull = maybeNullValues.Select(IsNull).Aggregate(Combining(IrBinaryOp.LogicalOr));

        return new IrConditionalExpression(anyNull, NullLiteral, applied);
    }

    private IrExpression LiftUnary(OperatorOperand operand, Func<IrExpression, IrExpression> apply)
    {
        if (!operand.MayBeNull)
            return apply(operand.Value);

        if (ConditionalAccessOf(operand) is var (access, continuation))
            return access with { WhenNotNull = LiftUnary(continuation, apply) };

        return BindValueOnce(
            operand.Value,
            bound => new IrConditionalExpression(IsNull(bound), NullLiteral, apply(bound))
        );
    }

    private static ExpressionSyntax LastContinuation(ConditionalAccessExpressionSyntax access) =>
        access.WhenNotNull is ConditionalAccessExpressionSyntax nested
            ? LastContinuation(nested)
            : access.WhenNotNull;

    private static ExpressionSyntax Unparenthesized(ExpressionSyntax syntax) =>
        syntax is ParenthesizedExpressionSyntax parenthesized
            ? Unparenthesized(parenthesized.Expression)
            : syntax;

    private static IrLiteral NullResultOf(LiftedOperator op) =>
        op.IsRelational ? FalseLiteral : NullLiteral;

    private static Func<IrExpression, IrExpression, IrExpression> Combining(IrBinaryOp op) =>
        (left, right) => new IrBinaryExpression(left, op, right);

    private static IrExpression IsNull(IrExpression value) =>
        new IrBinaryExpression(value, IrBinaryOp.Equal, NullLiteral);

    private static IrExpression IsNotNull(IrExpression value) =>
        new IrBinaryExpression(value, IrBinaryOp.NotEqual, NullLiteral);
}
