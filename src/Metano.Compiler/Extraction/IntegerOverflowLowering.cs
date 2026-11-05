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
/// (DECIDED-7).
/// </summary>
internal static class IntegerOverflowLowering
{
    /// <summary>
    /// The reduction <paramref name="operation"/> needs: <c>+</c>, <c>-</c>,
    /// <c>*</c>, unary <c>-</c>, <c>++</c>, <c>--</c> and the compound
    /// <c>+=</c>, <c>-=</c>, <c>*=</c> on a 64-bit integer (lifted forms
    /// included), and <c>~</c> on <c>ulong</c>, which always wraps;
    /// <c>null</c> for any other operation, a user-defined operator, or one
    /// narrower than 64 bits (OQ-4).
    /// </summary>
    public static IntegerOverflowReduction? ReductionOf(IOperation? operation) =>
        operation switch
        {
            IBinaryOperation binary => BinaryReduction(binary),
            ICompoundAssignmentOperation compound => CompoundReduction(compound),
            IIncrementOrDecrementOperation { OperatorMethod: null } increment => ReductionIn(
                increment.Type,
                increment.IsChecked
            ),
            IUnaryOperation { OperatorMethod: null } unary => UnaryReduction(unary),
            _ => null,
        };

    private static IntegerOverflowReduction? BinaryReduction(IBinaryOperation binary) =>
        IsBuiltInArithmetic(binary.OperatorMethod, binary.OperatorKind)
            ? ReductionIn(binary.Type, binary.IsChecked)
            : null;

    /// <summary>
    /// A compound assignment stores its operator's result in the target's
    /// type; only an identity store keeps the 64-bit result (C# rejects the
    /// other 64-bit forms).
    /// </summary>
    private static IntegerOverflowReduction? CompoundReduction(
        ICompoundAssignmentOperation compound
    ) =>
        IsBuiltInArithmetic(compound.OperatorMethod, compound.OperatorKind)
        && compound.OutConversion.IsIdentity
            ? ReductionIn(compound.Type, compound.IsChecked)
            : null;

    private static IntegerOverflowReduction? UnaryReduction(IUnaryOperation unary) =>
        unary.OperatorKind switch
        {
            UnaryOperatorKind.Minus => ReductionIn(unary.Type, unary.IsChecked),
            UnaryOperatorKind.BitwiseNegation => UnsignedComplementReduction(unary.Type),
            _ => null,
        };

    private static bool IsBuiltInArithmetic(
        IMethodSymbol? operatorMethod,
        BinaryOperatorKind kind
    ) => operatorMethod is null && IsArithmetic(kind);

    private static bool IsArithmetic(BinaryOperatorKind kind) =>
        kind
            is BinaryOperatorKind.Add
                or BinaryOperatorKind.Subtract
                or BinaryOperatorKind.Multiply;

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

    private static IrNumericKind? SixtyFourBitKindOf(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { } endpoint && endpoint.IsSixtyFourBitValue()
            ? endpoint.Kind
            : null;
}
