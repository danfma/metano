using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The stores into a <c>decimal</c> for a target that computes with it as a
/// runtime value whose operators are methods
/// (<see cref="NumericLoweringRules.LowersDecimalValues"/>): a compound
/// assignment stores the method call (<c>d += x</c> → <c>d = d.plus(x)</c>)
/// and <c>++</c> / <c>--</c> store the stepped value
/// (<c>d = d.plus(1)</c>), through the read-modify-write that evaluates the
/// target's receiver and index once. The native operators would coerce the
/// value to a string or a number.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private static readonly IrLiteral DecimalOne = new("1", IrLiteralKind.Decimal);

    /// <summary>Whether <paramref name="operand"/> is a non-nullable <c>decimal</c> whose stores go through its methods.</summary>
    private bool IsDecimalStoreTarget(ExpressionSyntax operand) =>
        _context.NumericLowering.LowersDecimalValues
        && _semantic.GetTypeInfo(operand).Type?.SpecialType is SpecialType.System_Decimal;

    /// <summary>A compound assignment to a <c>decimal</c> whose operator is a method of the runtime value.</summary>
    private bool IsDecimalCompound(IrBinaryOp op, AssignmentExpressionSyntax assign) =>
        MapDecimalBinaryMethod(op) is not null && IsDecimalStoreTarget(assign.Left);

    /// <summary><c>d++</c>, <c>++d</c>, <c>d--</c> or <c>--d</c> on a <c>decimal</c>.</summary>
    private IrExpression ExtractDecimalIncrement(
        ExpressionSyntax node,
        IrUnaryOp op,
        IrExpression target,
        bool isPrefix
    ) =>
        WriteIncrement(node, target, isPrefix, current =>
            BuildDecimalBinaryCall(current, DecimalOne, MapDecimalBinaryMethod(StepOperatorOf(op))!)
        );
}
