using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The stores into a <c>char</c> (spec 001 FR-072): <c>++</c>, <c>--</c> and a
/// compound assignment read the UTF-16 code of the target, apply the operator
/// on the <c>int</c> C# computes in, and store the result back as a
/// <c>char</c> (<c>c = (char)(c + 1)</c>), wrapped or range-checked as the
/// context says. A <c>char?</c> target lifts the store: <c>null</c> stays
/// <c>null</c> (#695). The conversions are neutral nodes; each target decides
/// how it prints them.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private static readonly IrNumericEndpoint CharEndpoint = new(IrNumericKind.Char);
    private static readonly IrNumericEndpoint Int32Endpoint = new(IrNumericKind.Int32);

    /// <summary>Whether <paramref name="operand"/> is a <c>char</c> or a <c>char?</c>: a target whose store goes through the code.</summary>
    private bool IsCharStoreTarget(ExpressionSyntax operand) =>
        _context.NumericLowering.LowersCharCodes
        && _semantic.GetTypeInfo(operand).Type?.UnwrapNullableValueType().SpecialType
            is SpecialType.System_Char;

    /// <summary><c>c++</c>, <c>++c</c>, <c>c--</c> or <c>--c</c> on a <c>char</c> or a <c>char?</c> (FR-072).</summary>
    private IrExpression ExtractCharIncrement(
        ExpressionSyntax node,
        ExpressionSyntax operandSyntax,
        IrUnaryOp op,
        IrExpression target,
        bool isPrefix
    )
    {
        var isChecked = CheckedContexts.IsChecked(node, _semantic.Compilation);
        var mayBeNull = IsNullableValueOperand(operandSyntax);

        return WriteIncrement(node, target, isPrefix, current =>
            LiftUnary(new OperatorOperand(current, mayBeNull, operandSyntax), value =>
                CharStore(
                    new IrBinaryExpression(CharCode(value), StepOperatorOf(op), OneLiteral),
                    isChecked
                )
            )
        );
    }

    /// <summary><c>c op= right</c> on a <c>char</c> or a <c>char?</c>: <c>c = (char)(code(c) op right)</c> (FR-072).</summary>
    private IrExpression BuildCharCompoundAssignment(
        IrExpression target,
        AssignmentExpressionSyntax assign,
        IrBinaryOp op,
        IrExpression right
    )
    {
        var isChecked = CheckedContexts.IsChecked(assign, _semantic.Compilation);

        var store = new LiftedOperator(IsRelational: false, (left, rightOperand) =>
            CharStore(BuildNumericBinary(assign, op, CharCode(left.Value), rightOperand), isChecked)
        );

        return BuildReadModifyWrite(target, assign, readTarget =>
            LiftBinary(
                new OperatorOperand(readTarget, IsNullableValueOperand(assign.Left), assign.Left),
                new OperatorOperand(right, IsNullableValueOperand(assign.Right), assign.Right),
                store
            )
        );
    }

    private static IrNumericConversion CharCode(IrExpression value) =>
        new(value, CharEndpoint, Int32Endpoint, IsChecked: false, IsLifted: false);

    private static IrNumericConversion CharStore(IrExpression value, bool isChecked) =>
        new(value, Int32Endpoint, CharEndpoint, isChecked, IsLifted: false);
}
