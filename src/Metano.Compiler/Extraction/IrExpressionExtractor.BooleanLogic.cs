using System.Diagnostics;
using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// C#'s <c>&amp;</c>, <c>|</c> and <c>^</c> over booleans (and their compound
/// forms) evaluate both operands, unlike <c>&amp;&amp;</c> / <c>||</c>, and
/// JavaScript applies them to numbers. Over <c>bool</c> they become
/// <see cref="IrBinaryOp.BooleanAnd"/>, <see cref="IrBinaryOp.BooleanOr"/>
/// and <see cref="IrBinaryOp.BooleanXor"/> for each backend to lower. Over
/// <c>bool?</c> they follow three-valued logic, lowered here, over the
/// ordinary IR, with both operands bound once in source order:
/// <list type="bullet">
///   <item><c>&amp;</c> is <c>false</c> when either operand is, else
///     <c>null</c> when either is, else <c>true</c>;</item>
///   <item><c>|</c> is <c>true</c> when either operand is, else <c>null</c>
///     when either is, else <c>false</c>;</item>
///   <item><c>^</c> is <c>null</c> when either operand is, else whether they
///     differ.</item>
/// </list>
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The boolean form of a bitwise <paramref name="op"/> when
    /// <paramref name="operation"/> is a built-in operator over non-nullable
    /// <c>bool</c> operands; a lifted <c>bool?</c> operation keeps
    /// <paramref name="op"/> for the three-valued lowering.
    /// </summary>
    private IrBinaryOp NarrowToBooleanLogic(IrBinaryOp op, ExpressionSyntax operation) =>
        op.IsBitwise()
        && _semantic.GetOperation(operation) is { } bound
        && IsBuiltinBooleanLogic(bound)
        && bound.Type?.SpecialType == SpecialType.System_Boolean
            ? op switch
            {
                IrBinaryOp.BitwiseAnd => IrBinaryOp.BooleanAnd,
                IrBinaryOp.BitwiseOr => IrBinaryOp.BooleanOr,
                IrBinaryOp.BitwiseXor => IrBinaryOp.BooleanXor,
                _ => throw new UnreachableException($"'{op}' is not a bitwise operator."),
            }
            : op;

    /// <summary>
    /// Whether <paramref name="operation"/> is a built-in operator over
    /// <c>bool</c> or <c>bool?</c> operands, as opposed to an enum or integer
    /// operator or a user-defined one.
    /// </summary>
    private static bool IsBuiltinBooleanLogic(IOperation operation)
    {
        var (isOperator, method) = operation switch
        {
            IBinaryOperation binary => (true, binary.OperatorMethod),
            ICompoundAssignmentOperation compound => (true, compound.OperatorMethod),
            _ => (false, null),
        };

        return isOperator
            && (method is null or { MethodKind: MethodKind.BuiltinOperator })
            && operation.Type.UnderlyingValueType()?.SpecialType == SpecialType.System_Boolean;
    }

    /// <summary>
    /// Whether <paramref name="operation"/> is a built-in <c>bool?</c>
    /// <c>&amp;</c>, <c>|</c> or <c>^</c> (lifted), which follows
    /// three-valued logic.
    /// </summary>
    private static bool IsNullableBooleanLogic(IOperation? operation, IrBinaryOp op) =>
        op.IsBitwise()
        && operation is not null
        && IsBuiltinBooleanLogic(operation)
        && operation.Type.IsNullableValueType();

    /// <summary>
    /// <c>left op right</c> over <c>bool?</c> operands, or <c>null</c> when
    /// the operation is not one.
    /// </summary>
    private IrExpression? TryExtractNullableBooleanLogic(
        BinaryExpressionSyntax bin,
        IrBinaryOp op
    ) =>
        IsNullableBooleanLogic(_semantic.GetOperation(bin), op)
            ? LiftBooleanLogic(OperandOf(bin.Left), OperandOf(bin.Right), op)
            : null;

    /// <summary>
    /// <c>target op= value</c> over <c>bool?</c> operands:
    /// <c>target = target op value</c> with the three-valued operator.
    /// </summary>
    private IrExpression? TryExtractNullableBooleanCompoundAssignment(
        AssignmentExpressionSyntax assign,
        IrExpression target
    )
    {
        if (
            TryMapCompoundAssignmentToBinary(assign.Kind()) is not { } op
            || !IsNullableBooleanLogic(_semantic.GetOperation(assign), op)
        )
            return null;

        return BuildReadModifyWrite(
            target,
            assign,
            readTarget =>
                LiftBooleanLogic(
                    new OperatorOperand(
                        readTarget,
                        IsNullableValueOperand(assign.Left),
                        assign.Left
                    ),
                    OperandOf(assign.Right),
                    op
                )
        );
    }

    private IrExpression LiftBooleanLogic(
        OperatorOperand left,
        OperatorOperand right,
        IrBinaryOp op
    ) =>
        BindOperandsInOrder(
            left,
            right,
            (boundLeft, boundRight) => ThreeValuedLogic(boundLeft, boundRight, op)
        );

    /// <summary>
    /// The three-valued result over bound operands: the value that decides
    /// the outcome on its own (<c>false</c> for <c>&amp;</c>, <c>true</c> for
    /// <c>|</c>) wins over <c>null</c>, which wins over the ordinary result.
    /// A <c>null</c> literal can never decide, and makes the outcome
    /// <c>null</c> unless the other operand does.
    /// </summary>
    private static IrExpression ThreeValuedLogic(
        OperatorOperand left,
        OperatorOperand right,
        IrBinaryOp op
    )
    {
        var nonNullLiteralOperands = new[] { left, right }
            .Where(operand => operand.Value is not IrLiteral { Kind: IrLiteralKind.Null })
            .ToList();

        if (nonNullLiteralOperands.Count == 0)
            return NullLiteral;

        var hasNullLiteral = nonNullLiteralOperands.Count < 2;

        if (op is IrBinaryOp.BitwiseXor)
            return NullUnless(
                nonNullLiteralOperands,
                hasNullLiteral,
                new IrBinaryExpression(left.Value, IrBinaryOp.BooleanXor, right.Value)
            );

        var isAnd = op is IrBinaryOp.BitwiseAnd;
        var decisive = isAnd ? FalseLiteral : TrueLiteral;
        var undecided = isAnd ? TrueLiteral : FalseLiteral;

        var anyDecisive = nonNullLiteralOperands
            .Select(operand => Decides(operand, decisive))
            .Aggregate(Combining(IrBinaryOp.LogicalOr));

        return new IrConditionalExpression(
            anyDecisive,
            decisive,
            NullUnless(nonNullLiteralOperands, hasNullLiteral, undecided)
        );
    }

    /// <summary>
    /// <paramref name="result"/> when no operand is <c>null</c>, else
    /// <c>null</c>: a <c>null</c> literal operand makes it unconditional,
    /// and an operand that cannot be <c>null</c> needs no test.
    /// </summary>
    private static IrExpression NullUnless(
        IReadOnlyList<OperatorOperand> nonNullLiteralOperands,
        bool hasNullLiteral,
        IrExpression result
    )
    {
        if (hasNullLiteral)
            return NullLiteral;

        var nullTests = nonNullLiteralOperands
            .Where(operand => operand.MayBeNull)
            .Select(operand => IsNull(operand.Value))
            .ToList();

        return nullTests.Count == 0
            ? result
            : new IrConditionalExpression(
                nullTests.Aggregate(Combining(IrBinaryOp.LogicalOr)),
                NullLiteral,
                result
            );
    }

    /// <summary>
    /// Whether the operand alone decides the outcome, by equalling the
    /// <paramref name="decisive"/> value: <c>x == false</c> for a nullable
    /// operand, which a <c>null</c> never equals, and <c>!x</c> or <c>x</c>
    /// otherwise.
    /// </summary>
    private static IrExpression Decides(OperatorOperand operand, IrLiteral decisive)
    {
        if (operand.MayBeNull)
            return new IrBinaryExpression(operand.Value, IrBinaryOp.Equal, decisive);

        return decisive.Value is true
            ? operand.Value
            : new IrUnaryExpression(IrUnaryOp.LogicalNot, operand.Value);
    }
}
