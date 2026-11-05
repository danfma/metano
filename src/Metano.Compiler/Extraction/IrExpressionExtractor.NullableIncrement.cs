using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>++</c> and <c>--</c> over a nullable value (<c>int?</c>): the operator
/// is lifted, so <c>null</c> stays <c>null</c>. The JS operators would turn
/// it into <c>1</c> / <c>-1</c>, so the increment lowers to the lifted
/// <c>target = target + 1</c>, evaluating the target's receiver and index
/// once. A prefix form, and a postfix form whose value is unused, are that
/// assignment; a postfix form whose value is used binds the previous value
/// first and yields it after the write.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private static readonly IrLiteral OneLiteral = new(1, IrLiteralKind.Int32);

    private IrExpression LiftIncrement(
        ExpressionSyntax node,
        ExpressionSyntax operandSyntax,
        IrUnaryOp op,
        IrExpression target,
        bool isPrefix
    )
    {
        var lifted = LiftedOperatorFor(StepOperatorOf(op), _semantic.GetOperation(node));

        IrExpression Increment(IrExpression current) =>
            LiftBinary(
                new OperatorOperand(current, MayBeNull: true, operandSyntax),
                new OperatorOperand(OneOf(operandSyntax), MayBeNull: false, operandSyntax),
                lifted
            );

        return WriteIncrement(node, target, isPrefix, Increment);
    }

    /// <summary>
    /// Writes <paramref name="increment"/> of <paramref name="target"/> back
    /// to it, evaluating the target's receiver and index once. A prefix form,
    /// and a postfix form whose value is unused, are that assignment; a
    /// postfix form whose value is used binds the previous value first and
    /// yields it after the write.
    /// </summary>
    private IrExpression WriteIncrement(
        ExpressionSyntax node,
        IrExpression target,
        bool isPrefix,
        Func<IrExpression, IrExpression> increment
    ) =>
        BindTargetOnce(
            target,
            node,
            readTarget =>
                isPrefix || IsValueUnused(node)
                    ? WriteInPlace(readTarget, increment)
                    : WriteAndYieldPrevious(readTarget, increment)
        );

    /// <summary>
    /// The step <c>1</c> in the operand's numeric representation: a
    /// <c>BigInteger</c> is a <c>bigint</c>, which JS does not mix with a
    /// <c>number</c>, and so is a 64-bit integer under the exact lowering
    /// (a <c>1n</c> literal of its kind).
    /// </summary>
    private IrExpression OneOf(ExpressionSyntax operand)
    {
        var type = _semantic.GetTypeInfo(operand).Type;

        if (type.UnderlyingValueType()?.ToDisplayString() == "System.Numerics.BigInteger")
            return new IrCallExpression(
                new IrTypeReference("BigInt"),
                [new IrArgument(OneLiteral)]
            );

        return NumericEndpoints.Of(type) is { } endpoint && endpoint.IsSixtyFourBitValue()
            ? SixtyFourBitOne(endpoint.Kind)
            : OneLiteral;
    }

    /// <summary>
    /// <c>(previous = target, target = compute(previous), previous)</c>: the
    /// value of a postfix increment is the one the target held before.
    /// </summary>
    private IrExpression WriteAndYieldPrevious(
        IrExpression target,
        Func<IrExpression, IrExpression> compute
    ) =>
        BindOnce(
            target,
            isReusable: false,
            previous => new IrSequenceExpression(
                [new IrBinaryExpression(target, IrBinaryOp.Assign, compute(previous))],
                previous
            )
        );

    private static bool IsValueUnused(ExpressionSyntax node) =>
        node.Parent is ExpressionStatementSyntax
        || (node.Parent is ForStatementSyntax loop && loop.Incrementors.Contains(node));

    private static IrLiteral SixtyFourBitOne(IrNumericKind kind) =>
        kind is IrNumericKind.UInt64
            ? new IrLiteral(1UL, IrLiteralKind.UInt64)
            : new IrLiteral(1L, IrLiteralKind.Int64);

    private static IrBinaryOp StepOperatorOf(IrUnaryOp op) =>
        op is IrUnaryOp.Increment ? IrBinaryOp.Add : IrBinaryOp.Subtract;
}
