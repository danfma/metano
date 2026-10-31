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
    /// <c>/=</c> has no operator in the targets that keeps C# semantics, so it
    /// becomes the read-modify-write <c>target = target IntegerDivide right</c>
    /// (see <see cref="BuildIntegerDivisionAssignment"/>).
    /// </summary>
    private IrExpression BuildAssignmentWithIntegerDivision(
        IrExpression target,
        AssignmentExpressionSyntax assign
    )
    {
        var right = Extract(assign.Right);

        if (IsIntegerDivisionAssignment(assign))
            return BuildIntegerDivisionAssignment(target, right, assign);

        return new IrBinaryExpression(target, MapAssignmentOp(assign.Kind()), right);
    }

    private bool IsIntegerDivisionAssignment(AssignmentExpressionSyntax assign) =>
        assign.IsKind(SyntaxKind.DivideAssignmentExpression) && IsIntegerDivision(assign);

    /// <summary>
    /// <c>target = target IntegerDivide divisor</c>, reading the target twice.
    /// The receiver of a member access and the receiver and index of an
    /// element access are evaluated once, in source order, as C# does for a
    /// compound assignment: a reusable one is repeated, anything else is
    /// let-bound first. Any other assignable shape is reported as unsupported
    /// rather than evaluated twice, and so is a dictionary entry: the read
    /// of a missing key would divide <c>undefined</c> where C# throws
    /// <c>KeyNotFoundException</c>.
    /// </summary>
    private IrExpression BuildIntegerDivisionAssignment(
        IrExpression target,
        IrExpression divisor,
        AssignmentExpressionSyntax assign
    ) =>
        target switch
        {
            IrElementAccess { TargetType: IrMapTypeRef } => ReportUnsupported(
                assign,
                assign.Kind().ToString(),
                $"Integer division assignment '{assign}' on a dictionary entry is not supported."
            ),
            _ when IrEvaluationTraits.IsReusableReceiver(target) => DivideInPlace(target, divisor),
            IrMemberAccess member => DivideMemberInPlace(member, divisor),
            IrElementAccess element => DivideElementInPlace(element, divisor),
            _ => ReportUnsupported(
                assign,
                assign.Kind().ToString(),
                $"Integer division assignment '{assign}' is not supported: its target cannot be "
                    + "read and written without evaluating it twice."
            ),
        };

    private IrExpression DivideMemberInPlace(IrMemberAccess member, IrExpression divisor) =>
        BindReceiverOnce(
            member.Target,
            receiver => DivideInPlace(member with { Target = receiver }, divisor)
        );

    private IrExpression DivideElementInPlace(IrElementAccess element, IrExpression divisor) =>
        BindReceiverOnce(
            element.Target,
            receiver =>
                BindValueOnce(
                    element.Index,
                    index =>
                        DivideInPlace(element with { Target = receiver, Index = index }, divisor)
                )
        );

    private static IrBinaryExpression DivideInPlace(IrExpression target, IrExpression divisor) =>
        new(
            target,
            IrBinaryOp.Assign,
            new IrBinaryExpression(target, IrBinaryOp.IntegerDivide, divisor)
        );

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
