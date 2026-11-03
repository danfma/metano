using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Separates C#'s integer division from floating-point division. Both share
/// the <c>/</c> token, but over integral operands C# truncates the quotient
/// toward zero and throws on a zero divisor, so the extractor marks them as
/// <see cref="IrBinaryOp.IntegerDivide"/> for each backend to lower.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <see cref="IrBinaryOp.IntegerDivide"/> when <paramref name="op"/> is
    /// <see cref="IrBinaryOp.Divide"/> and <paramref name="operation"/> (a
    /// binary <c>/</c> or a compound <c>/=</c>) binds to an integer division;
    /// <paramref name="op"/> unchanged otherwise.
    /// </summary>
    private IrBinaryOp NarrowToIntegerDivision(IrBinaryOp op, ExpressionSyntax operation) =>
        op is IrBinaryOp.Divide && IsIntegerDivision(operation) ? IrBinaryOp.IntegerDivide : op;

    /// <summary>
    /// Whether Roslyn bound <paramref name="operation"/> to a built-in
    /// division whose result is integral. Byte, short and char operands are
    /// promoted to <c>int</c> first, so the result type covers them, and a
    /// lifted <c>int?</c> division reports its nullable result. User-defined
    /// operators (<c>BigInteger</c>, user structs) and the floating-point and
    /// decimal operators do not match.
    /// </summary>
    private bool IsIntegerDivision(ExpressionSyntax operation) =>
        _semantic.GetSymbolInfo(operation).Symbol
            is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator } divide
        && IsIntegralNumber(UnwrapNullable(divide.ReturnType));

    /// <summary>
    /// Builds <paramref name="target"/> <c>op=</c> <c>right</c>. An integer
    /// <c>/=</c> and a boolean <c>&amp;=</c> / <c>|=</c> / <c>^=</c> have no
    /// compound operator in the targets that keeps C# semantics, so each
    /// becomes the read-modify-write <c>target = target op right</c> (see
    /// <see cref="BuildReadModifyWrite"/>).
    /// </summary>
    private IrExpression BuildCompoundAssignment(
        IrExpression target,
        AssignmentExpressionSyntax assign
    )
    {
        var right = Extract(assign.Right);

        if (MapCompoundAssignmentOperator(assign) is { } op && HasNoCompoundForm(op))
            return BuildReadModifyWrite(
                target,
                assign,
                readTarget => new IrBinaryExpression(readTarget, op, right)
            );

        return new IrBinaryExpression(target, MapAssignmentOp(assign.Kind()), right);
    }

    private static bool HasNoCompoundForm(IrBinaryOp op) =>
        op
            is IrBinaryOp.IntegerDivide
                or IrBinaryOp.BooleanAnd
                or IrBinaryOp.BooleanOr
                or IrBinaryOp.BooleanXor;

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

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static bool IsIntegralNumber(ITypeSymbol type) =>
        type.SpecialType
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr
                or SpecialType.System_Char;
}
