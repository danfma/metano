using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The built-in numeric binary operators of spec 012 slice 012d (FR-027–FR-030):
/// each node carries its operand kind, so a target picks the 64-bit division
/// helpers and converts a shift count without re-deriving types, and under
/// the exact 64-bit lowering a 64-bit shift count is masked as C# masks it
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
    /// <c>left op right</c> as <paramref name="operation"/> (a binary
    /// expression or a compound assignment) binds it, with the operand kind
    /// of a built-in numeric operator and a 64-bit shift count masked;
    /// <paramref name="rightSyntax"/> folds a constant count.
    /// </summary>
    private IrBinaryExpression NumericBinary(
        ExpressionSyntax operation,
        IrExpression left,
        IrBinaryOp op,
        IrExpression right,
        ExpressionSyntax rightSyntax
    )
    {
        var kind = BuiltInNumericKindOf(operation);

        return new IrBinaryExpression(left, op, ShiftCountOf(op, kind, right, rightSyntax), kind);
    }

    /// <summary>
    /// The value <c>target op= right</c> stores: the binary form over the
    /// current value, reduced as the compound operation needs (FR-024).
    /// </summary>
    private IrExpression CompoundValue(
        AssignmentExpressionSyntax assign,
        IrExpression current,
        IrBinaryOp op,
        IrExpression right
    ) =>
        ReduceOverflow(
            _semantic.GetOperation(assign),
            NumericBinary(assign, current, op, right, assign.Right)
        );

    /// <summary>
    /// The operand kind of the built-in operator <paramref name="operation"/>
    /// binds to: its left parameter's kind, through <c>Nullable&lt;T&gt;</c>
    /// for a lifted operator; <c>null</c> for an enum, a user-defined, a
    /// string or a boolean operator.
    /// </summary>
    private IrNumericKind? BuiltInNumericKindOf(ExpressionSyntax operation) =>
        _semantic.GetSymbolInfo(operation).Symbol
            is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator, Parameters: [var left, ..] }
        && NumericEndpoints.Of(left.Type) is { IsEnum: false } endpoint
            ? endpoint.Kind
            : null;

    /// <summary>
    /// The count of a 64-bit shift under the exact 64-bit lowering, as C#
    /// reads it: <c>count &amp; 63</c>, folded into a literal when constant
    /// (<c>l &lt;&lt; 70</c> shifts by <c>6</c>, <c>l &lt;&lt; -1</c> by
    /// <c>63</c>). Any other right operand is unchanged; the shipped
    /// <c>Number</c> mode keeps its shifts (DECIDED-22).
    /// </summary>
    private IrExpression ShiftCountOf(
        IrBinaryOp op,
        IrNumericKind? kind,
        IrExpression count,
        ExpressionSyntax countSyntax
    )
    {
        var masksCount =
            _context.ExactInt64Lowering
            && op.IsShift()
            && kind is { } shifted
            && shifted.IsSixtyFourBit();

        if (!masksCount)
            return count;

        if (_semantic.GetConstantValue(countSyntax) is { HasValue: true, Value: { } constant })
            return new IrLiteral(
                Convert.ToInt32(constant, CultureInfo.InvariantCulture)
                    & SixtyFourBitShiftCountMask,
                IrLiteralKind.Int32
            );

        return new IrBinaryExpression(
            count,
            IrBinaryOp.BitwiseAnd,
            SixtyFourBitShiftCountMaskLiteral,
            IrNumericKind.Int32
        );
    }

    /// <summary>
    /// Whether <paramref name="op"/> on 64-bit operands has no compound
    /// operator in the targets that keeps C# semantics under the exact 64-bit
    /// lowering: division and remainder throw where the native operators do
    /// not, and a shift masks (and in TypeScript converts) its count, so their
    /// compound forms read, compute and write the target (FR-024).
    /// <c>&amp;=</c>, <c>|=</c> and <c>^=</c> keep the native operator.
    /// </summary>
    private bool NeedsSixtyFourBitReadModifyWrite(IrBinaryOp op, ExpressionSyntax operation) =>
        _context.ExactInt64Lowering
        && (op is IrBinaryOp.IntegerDivide or IrBinaryOp.Modulo || op.IsShift())
        && BuiltInNumericKindOf(operation) is { } kind
        && kind.IsSixtyFourBit();
}
