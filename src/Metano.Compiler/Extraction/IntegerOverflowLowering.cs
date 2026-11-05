using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The 64-bit overflow reduction of spec 012 (FR-021, FR-022, FR-026):
/// which built-in operations can leave the 64-bit domain, and whether the
/// result wraps or is range-checked. Roslyn decides both: the operation's
/// result type gives the kind, and its <c>IsChecked</c> flag carries the
/// <c>checked</c> / <c>unchecked</c> context and the compilation's
/// <c>CheckOverflow</c> option, lambdas included
/// (DECIDED-4). Operators that cannot leave the domain are never reduced
/// (DECIDED-7), and shifts wrap in either context (FR-022, FR-028).
/// </summary>
internal static class IntegerOverflowLowering
{
    /// <summary>
    /// The reduction <paramref name="operation"/> needs: <c>+</c>, <c>-</c>,
    /// <c>*</c>, unary <c>-</c>, <c>++</c>, <c>--</c> and the compound
    /// <c>+=</c>, <c>-=</c>, <c>*=</c> on a 64-bit integer (lifted forms
    /// included); <c>~</c> on <c>ulong</c>, <c>&lt;&lt;</c> and
    /// <c>&gt;&gt;&gt;</c> on <c>long</c> (and their compound forms), which
    /// always wrap; <c>null</c> for any other operation, a user-defined
    /// operator, or one narrower than 64 bits (OQ-4).
    /// </summary>
    public static IntegerOverflowReduction? ReductionOf(IOperation? operation) =>
        operation switch
        {
            IBinaryOperation { OperatorMethod: null } binary => BinaryReduction(
                binary.OperatorKind,
                binary.Type,
                binary.IsChecked
            ),
            ICompoundAssignmentOperation { OperatorMethod: null } compound => CompoundReduction(
                compound
            ),
            IIncrementOrDecrementOperation { OperatorMethod: null } increment => ReductionIn(
                increment.Type,
                increment.IsChecked
            ),
            IUnaryOperation { OperatorMethod: null } unary => UnaryReduction(unary),
            _ => null,
        };

    /// <summary>
    /// A compound assignment stores its operator's result in the target's
    /// type; only an identity store keeps the 64-bit result (C# rejects the
    /// other 64-bit forms).
    /// </summary>
    private static IntegerOverflowReduction? CompoundReduction(
        ICompoundAssignmentOperation compound
    ) =>
        compound.OutConversion.IsIdentity
            ? BinaryReduction(compound.OperatorKind, compound.Type, compound.IsChecked)
            : null;

    /// <summary>
    /// <c>&lt;&lt;</c> drops the bits shifted past bit 63 in either context
    /// (shifts never check, FR-022); <c>&gt;&gt;&gt;</c> on a <c>long</c>
    /// shifts its unsigned bits, which read back as signed (FR-029).
    /// </summary>
    private static IntegerOverflowReduction? BinaryReduction(
        BinaryOperatorKind kind,
        ITypeSymbol? type,
        bool isChecked
    ) =>
        kind switch
        {
            BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply =>
                ReductionIn(type, isChecked),
            BinaryOperatorKind.LeftShift => ReductionIn(type, isChecked: false),
            BinaryOperatorKind.UnsignedRightShift => SignedWrapIn(type),
            _ => null,
        };

    private static IntegerOverflowReduction? UnaryReduction(IUnaryOperation unary) =>
        unary.OperatorKind switch
        {
            UnaryOperatorKind.Minus => ReductionIn(unary.Type, unary.IsChecked),
            UnaryOperatorKind.BitwiseNegation => UnsignedComplementReduction(unary.Type),
            _ => null,
        };

    private static IntegerOverflowReduction? ReductionIn(ITypeSymbol? type, bool isChecked)
    {
        if (SixtyFourBitKindOf(type) is not { } kind)
            return null;

        return new IntegerOverflowReduction(
            kind,
            isChecked ? IrOverflowMode.Check : IrOverflowMode.Wrap
        );
    }

    /// <summary>
    /// <c>~u</c> on a <c>ulong</c> is negative as a <c>bigint</c>, so it wraps
    /// back into the unsigned domain; it cannot overflow, so it never checks.
    /// <c>~l</c> on a <c>long</c> stays in its domain and is not reduced.
    /// </summary>
    private static IntegerOverflowReduction? UnsignedComplementReduction(ITypeSymbol? type) =>
        SixtyFourBitKindOf(type) is IrNumericKind.UInt64
            ? new IntegerOverflowReduction(IrNumericKind.UInt64, IrOverflowMode.Wrap)
            : null;

    private static IntegerOverflowReduction? SignedWrapIn(ITypeSymbol? type) =>
        SixtyFourBitKindOf(type) is IrNumericKind.Int64
            ? new IntegerOverflowReduction(IrNumericKind.Int64, IrOverflowMode.Wrap)
            : null;

    private static IrNumericKind? SixtyFourBitKindOf(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { } endpoint && endpoint.IsSixtyFourBitValue()
            ? endpoint.Kind
            : null;
}
