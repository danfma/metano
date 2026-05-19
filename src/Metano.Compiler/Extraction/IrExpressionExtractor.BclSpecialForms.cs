using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// BCL-specific rewrite paths the extractor takes before falling
/// through to ordinary binary / call lowering. Today this covers:
/// <list type="bullet">
///   <item>Temporal-backed types (<c>DateTime</c>, <c>DateOnly</c>,
///   <c>TimeSpan</c>, …) → <c>Temporal.PlainXxx.compare(left, right) op 0</c>.</item>
///   <item><c>decimal</c> binary operators → <c>left.plus(right)</c>
///   et al on the runtime <c>Decimal</c> wrapper.</item>
///   <item><c>System.Math</c> static helpers (<c>Round</c>, <c>Floor</c>,
///   <c>Ceiling</c>, <c>Abs</c>) when the argument is <c>decimal</c>:
///   route through the same <c>Decimal</c> instance methods.</item>
/// </list>
/// Kept on the extractor partial so each rewriter shares the
/// <c>_semantic</c> field and the recursive <c>Extract</c> entry point
/// without the indirection of a sub-object.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Returns the target Temporal subtype name
    /// (<c>Temporal.PlainDate</c>, <c>Temporal.PlainDateTime</c>, …)
    /// when either operand of a binary expression is one of the
    /// Temporal-backed BCL types. Returns <c>null</c> otherwise so
    /// the caller falls through to the regular operator lowering.
    /// </summary>
    private string? GetTemporalTypeName(ExpressionSyntax left, ExpressionSyntax right) =>
        GetTemporalTypeName(left) ?? GetTemporalTypeName(right);

    private string? GetTemporalTypeName(ExpressionSyntax expression)
    {
        var info = _semantic.GetTypeInfo(expression);
        var type = info.ConvertedType ?? info.Type;
        if (type is null)
            return null;

        // Nullable-value wrappers (`DateOnly?`, `TimeSpan?`, …) still
        // trip the same runtime error when the inner value is used in
        // a relational operator. Peel `System.Nullable<T>` so the
        // underlying Temporal-backed type resolves against the map
        // below.
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments: [{ } inner],
            }
        )
            type = inner;

        // BCL types map to Temporal subtypes. `DateTime` / `TimeSpan`
        // carry `SpecialType`s so `OriginalDefinition.ToDisplayString`
        // is the reliable key here rather than the `SpecialType.None`
        // fast path.
        return type.OriginalDefinition.ToDisplayString() switch
        {
            "System.DateTime" => "Temporal.PlainDateTime",
            "System.DateTimeOffset" => "Temporal.ZonedDateTime",
            "System.DateOnly" => "Temporal.PlainDate",
            "System.TimeOnly" => "Temporal.PlainTime",
            "System.TimeSpan" => "Temporal.Duration",
            _ => null,
        };
    }

    private static IrBinaryOp? MapRelationalOp(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.GreaterThanExpression => IrBinaryOp.GreaterThan,
            SyntaxKind.GreaterThanOrEqualExpression => IrBinaryOp.GreaterThanOrEqual,
            SyntaxKind.LessThanExpression => IrBinaryOp.LessThan,
            SyntaxKind.LessThanOrEqualExpression => IrBinaryOp.LessThanOrEqual,
            _ => null,
        };

    private IrExpression BuildTemporalCompareCall(
        BinaryExpressionSyntax bin,
        IrBinaryOp op,
        string temporalTypeName
    )
    {
        // Emit the qualified Temporal type name as an IrTypeReference
        // so the bridge preserves the original PascalCase (type
        // references bypass the camelCase member-access policy that
        // would otherwise turn `.PlainDate` into `.plainDate`).
        var call = new IrCallExpression(
            new IrMemberAccess(new IrTypeReference(temporalTypeName), "compare"),
            [new IrArgument(Extract(bin.Left)), new IrArgument(Extract(bin.Right))]
        );
        return new IrBinaryExpression(call, op, new IrLiteral(0, IrLiteralKind.Int32));
    }

    private bool IsDecimalOperand(ExpressionSyntax expr)
    {
        var info = _semantic.GetTypeInfo(expr);
        var t = info.ConvertedType ?? info.Type;
        return t?.SpecialType == SpecialType.System_Decimal;
    }

    /// <summary>
    /// C# binary operator → decimal.js method name. Comparison forms that
    /// need a logical negation (<c>!=</c>) carry a leading <c>"!"</c> so the
    /// builder wraps the call in <c>IrUnaryExpression(LogicalNot, …)</c>.
    /// </summary>
    private static string? MapDecimalBinaryMethod(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.AddExpression => "plus",
            SyntaxKind.SubtractExpression => "minus",
            SyntaxKind.MultiplyExpression => "times",
            SyntaxKind.DivideExpression => "div",
            SyntaxKind.ModuloExpression => "mod",
            SyntaxKind.EqualsExpression => "eq",
            SyntaxKind.NotEqualsExpression => "!eq",
            SyntaxKind.LessThanExpression => "lt",
            SyntaxKind.GreaterThanExpression => "gt",
            SyntaxKind.LessThanOrEqualExpression => "lte",
            SyntaxKind.GreaterThanOrEqualExpression => "gte",
            _ => null,
        };

    private IrExpression BuildDecimalBinaryCall(BinaryExpressionSyntax bin, string method)
    {
        var negate = method.StartsWith('!');
        if (negate)
            method = method[1..];
        var left = Extract(bin.Left);
        var right = Extract(bin.Right);
        IrExpression call = new IrCallExpression(
            new IrMemberAccess(left, method),
            [new IrArgument(right)]
        );
        if (negate)
            call = new IrUnaryExpression(IrUnaryOp.LogicalNot, call);
        return call;
    }

    // `Math.{Round,Floor,Ceiling,Abs}(decimal)` and `decimal.Parse`
    // rewrites moved to `Invocation/IntrinsicBclLoweringTable.cs` as
    // a symbol-keyed dispatch table — see ADR-adjacent note in the
    // class docstring there.
}
