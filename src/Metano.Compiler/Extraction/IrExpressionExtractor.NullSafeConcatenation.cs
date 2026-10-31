using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// C# string concatenation (<c>+</c>, <c>+=</c>) and interpolation format a
/// <c>null</c> operand as the empty string, where JavaScript writes
/// <c>"null"</c> (<c>"n" + null</c> is <c>"nnull"</c>). An operand that may
/// be <c>null</c> — a nullable value, a <c>null</c> literal, or a reference
/// or type parameter whose nullable flow state is <em>maybe null</em> — is
/// therefore extracted as <c>operand ?? ""</c>. In a nullable-disabled
/// context a reference has no flow state and is left as is.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private static readonly IrLiteral EmptyStringLiteral = new("", IrLiteralKind.String);

    /// <summary>
    /// <c>left + right</c> when it is a string concatenation, with each
    /// operand that may be <c>null</c> formatted as <c>""</c>
    /// (<c>left + (right ?? "")</c>); <c>null</c> for any other operator.
    /// </summary>
    private IrExpression? TryExtractStringConcatenation(BinaryExpressionSyntax bin)
    {
        if (!IsStringConcatenation(bin))
            return null;

        return new IrBinaryExpression(
            ConcatenationOperand(bin.Left),
            IrBinaryOp.Add,
            ConcatenationOperand(bin.Right)
        );
    }

    /// <summary>
    /// <c>target += value</c> on a string: a <c>value</c> that may be
    /// <c>null</c> is appended as <c>""</c>, and so is a <c>target</c> that
    /// may be <c>null</c> and can be read twice
    /// (<c>target = (target ?? "") + value</c>).
    /// </summary>
    private IrExpression? TryExtractStringCompoundConcatenation(
        AssignmentExpressionSyntax assign,
        IrExpression target
    )
    {
        if (!IsStringConcatenation(assign))
            return null;

        var value = ConcatenationOperand(assign.Right);

        if (!MayFormatAsNull(assign.Left) || !IrEvaluationTraits.IsReusableReceiver(target))
            return new IrBinaryExpression(target, IrBinaryOp.AddAssign, value);

        var appended = new IrBinaryExpression(
            new IrBinaryExpression(target, IrBinaryOp.NullCoalescing, EmptyStringLiteral),
            IrBinaryOp.Add,
            value
        );

        return new IrBinaryExpression(target, IrBinaryOp.Assign, appended);
    }

    /// <summary>
    /// An interpolation hole formats a <c>null</c> value as <c>""</c>
    /// whatever its format or alignment clause, so a hole that may be
    /// <c>null</c> reads <c>value ?? ""</c>.
    /// </summary>
    private IrExpression ExtractInterpolationHole(InterpolationSyntax hole) =>
        ConcatenationOperand(hole.Expression);

    private bool IsStringConcatenation(ExpressionSyntax operatorSyntax) =>
        _semantic.GetSymbolInfo(operatorSyntax).Symbol
            is IMethodSymbol
            {
                MethodKind: MethodKind.BuiltinOperator,
                Name: WellKnownMemberNames.AdditionOperatorName,
                ContainingType.SpecialType: SpecialType.System_String,
            };

    private IrExpression ConcatenationOperand(ExpressionSyntax operand)
    {
        var value = Extract(operand);

        return MayFormatAsNull(operand)
            ? new IrBinaryExpression(value, IrBinaryOp.NullCoalescing, EmptyStringLiteral)
            : value;
    }

    private bool MayFormatAsNull(ExpressionSyntax operand)
    {
        var info = _semantic.GetTypeInfo(operand);

        return info.Type is null
            || info.Type.IsNullableValueType()
            || info.Nullability.FlowState == NullableFlowState.MaybeNull;
    }
}
