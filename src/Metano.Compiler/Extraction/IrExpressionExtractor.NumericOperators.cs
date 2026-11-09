using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The built-in numeric binary operators of spec 012 slice 012d (FR-027–FR-030):
/// each node carries its operand kind, so a target picks the 64-bit division
/// helpers and converts a shift count without re-deriving types, and a
/// 64-bit shift count is masked as C# masks it
/// (<c>count &amp; 63</c>, folded when constant, DECIDED-9).
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>The bits of a 64-bit shift count C# reads.</summary>
    private const int SixtyFourBitShiftCountMask = 63;

    private static readonly IrLiteral SixtyFourBitShiftCountMaskLiteral = new(
        SixtyFourBitShiftCountMask,
        IrLiteralKind.Int32
    );

    /// <summary>
    /// <c>left op right</c> as <paramref name="operatorSyntax"/> (a binary
    /// expression or a compound assignment) binds it, with the operand kind
    /// of a built-in numeric operator and a 64-bit shift count masked.
    /// </summary>
    private IrBinaryExpression BuildNumericBinary(
        ExpressionSyntax operatorSyntax,
        IrBinaryOp op,
        IrExpression left,
        OperatorOperand right
    )
    {
        var kind = BuiltInNumericKindOf(operatorSyntax);

        return new IrBinaryExpression(left, op, ShiftCountOf(op, kind, right), kind);
    }

    /// <summary>
    /// The operand kind of the built-in operator <paramref name="operatorSyntax"/>
    /// binds to: its left parameter's kind, through <c>Nullable&lt;T&gt;</c>
    /// for a lifted operator; <c>null</c> for an enum, a user-defined, a
    /// string or a boolean operator.
    /// </summary>
    private IrNumericKind? BuiltInNumericKindOf(ExpressionSyntax operatorSyntax) =>
        _semantic.GetSymbolInfo(operatorSyntax).Symbol
            is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator, Parameters: [var left, ..] }
        && NumericEndpoints.Of(left.Type) is { IsEnum: false } endpoint
            ? endpoint.Kind
            : null;

    /// <summary>
    /// The count of a 64-bit shift as C# reads it: <c>count &amp; 63</c>,
    /// folded into a literal when constant (<c>l &lt;&lt; 70</c> shifts by
    /// <c>6</c>, <c>l &lt;&lt; -1</c> by <c>63</c>). Any other right operand
    /// is unchanged.
    /// </summary>
    private IrExpression ShiftCountOf(IrBinaryOp op, IrNumericKind? kind, OperatorOperand count)
    {
        if (!op.IsShift() || !IsSixtyFourBit(kind))
            return count.Value;

        if (_semantic.GetConstantValue(count.Syntax) is { HasValue: true, Value: { } constant })
            return MaskedConstantCount(constant);

        return new IrBinaryExpression(
            count.Value,
            IrBinaryOp.BitwiseAnd,
            SixtyFourBitShiftCountMaskLiteral,
            IrNumericKind.Int32
        );
    }

    /// <summary>
    /// Whether <paramref name="op"/> on 64-bit operands has no compound
    /// operator in the targets that keeps C# semantics under the exact 64-bit
    /// lowering: division and remainder throw where the native operators do
    /// not, and a shift reads its count masked, so their compound forms
    /// store the binary form's value through the read-modify-write lowering
    /// (FR-024), as the wrapping operators do. <c>&amp;=</c>, <c>|=</c> and
    /// <c>^=</c> keep the native operator.
    /// </summary>
    private bool NeedsSixtyFourBitReadModifyWrite(IrBinaryOp op, ExpressionSyntax operatorSyntax) =>
        (op.IsDivisionOrRemainder() || op.IsShift())
        && IsSixtyFourBit(BuiltInNumericKindOf(operatorSyntax));

    /// <summary>A 64-bit operand kind.</summary>
    private static bool IsSixtyFourBit(IrNumericKind? kind) => kind?.IsSixtyFourBit() == true;

    /// <summary>A constant shift count as C# reads it for a 64-bit shift.</summary>
    private static IrLiteral MaskedConstantCount(object constant) =>
        new(
            Convert.ToInt32(constant, CultureInfo.InvariantCulture) & SixtyFourBitShiftCountMask,
            IrLiteralKind.Int32
        );
}
