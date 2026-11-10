using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The integer overflow reduction of spec 012 (FR-021, FR-022, FR-026,
/// FR-075–FR-077): which built-in operations can leave the domain of their
/// integer type, and whether the result wraps or is range-checked. Roslyn
/// decides both: the operation's result type gives the kind, and its
/// <c>IsChecked</c> flag carries the <c>checked</c> / <c>unchecked</c>
/// context and the compilation's <c>CheckOverflow</c> option, lambdas
/// included (DECIDED-4). Operators that cannot leave the domain are never
/// reduced (DECIDED-7), and shifts wrap in either context (FR-022, FR-028).
/// The 64-bit rules are here; <see cref="NarrowIntegerOverflow"/> holds the
/// rules of the integers of 32 bits or fewer.
/// </summary>
internal static class IntegerOverflowLowering
{
    /// <summary>
    /// The reduction <paramref name="operation"/> needs: <c>+</c>, <c>-</c>,
    /// <c>*</c>, unary <c>-</c>, <c>++</c>, <c>--</c> and the compound
    /// <c>+=</c>, <c>-=</c>, <c>*=</c> on a 64-bit integer (lifted forms
    /// included); <c>~</c> on <c>ulong</c> and <c>&lt;&lt;</c> (and
    /// <c>&lt;&lt;=</c>), which always wrap; the reductions of
    /// <see cref="NarrowIntegerOverflow"/> on a narrower integer;
    /// <c>null</c> for any other operation or a user-defined operator.
    /// </summary>
    public static IntegerOverflowReduction? ReductionOf(IOperation? operation) =>
        operation switch
        {
            IBinaryOperation { OperatorMethod: null } binary => BinaryReduction(binary),
            ICompoundAssignmentOperation { OperatorMethod: null } compound => CompoundReduction(
                compound
            ),
            IIncrementOrDecrementOperation { OperatorMethod: null } increment => IncrementReduction(
                increment
            ),
            IUnaryOperation { OperatorMethod: null } unary => UnaryReduction(unary),
            _ => null,
        };

    private static IntegerOverflowReduction? BinaryReduction(IBinaryOperation binary) =>
        IntegerKindOf(binary.Type) switch
        {
            null => null,
            { } kind when kind.IsSixtyFourBit() => SixtyFourBitBinaryReduction(
                binary.OperatorKind,
                kind,
                binary.IsChecked
            ),
            { } kind => NarrowIntegerOverflow.BinaryReduction(
                binary.OperatorKind,
                kind,
                binary.IsChecked,
                IntegerValueRanges.Of(binary.LeftOperand),
                IntegerValueRanges.Of(binary.RightOperand)
            ),
        };

    /// <summary>
    /// A compound assignment stores its operator's result in the target's
    /// type. Only an identity store keeps a 64-bit result (C# rejects the
    /// other 64-bit forms); a narrower target follows
    /// <see cref="NarrowIntegerOverflow.CompoundReduction"/>.
    /// </summary>
    private static IntegerOverflowReduction? CompoundReduction(
        ICompoundAssignmentOperation compound
    ) =>
        IntegerKindOf(compound.Type) switch
        {
            null => null,
            { } kind when kind.IsSixtyFourBit() => compound.OutConversion.IsIdentity
                ? SixtyFourBitBinaryReduction(compound.OperatorKind, kind, compound.IsChecked)
                : null,
            { } kind => NarrowIntegerOverflow.CompoundReduction(
                compound.OperatorKind,
                kind,
                IsCheckedStore(compound),
                IntegerValueRanges.Of(compound.Value)
            ),
        };

    /// <summary>
    /// Whether the value <paramref name="compound"/> stores is checked. A
    /// target narrower than <c>int</c> stores through the explicit conversion
    /// C# inserts, which a checked context checks whatever the operator
    /// (<c>checked { b &lt;&lt;= 1; }</c> throws), where Roslyn's
    /// <c>IsChecked</c> speaks for the operator alone (a shift never
    /// checks); the context of the syntax decides.
    /// </summary>
    private static bool IsCheckedStore(ICompoundAssignmentOperation compound) =>
        compound.IsChecked
        || (
            !compound.OutConversion.IsIdentity
            && compound.SemanticModel is { } semantic
            && CheckedContexts.IsChecked(compound.Syntax, semantic.Compilation)
        );

    private static IntegerOverflowReduction? IncrementReduction(
        IIncrementOrDecrementOperation increment
    ) =>
        IntegerKindOf(increment.Type) switch
        {
            null => null,
            { } kind when kind.IsSixtyFourBit() => IntegerOverflowReduction.Of(
                kind,
                increment.IsChecked
            ),
            { } kind => NarrowIntegerOverflow.IncrementReduction(kind, increment.IsChecked),
        };

    private static IntegerOverflowReduction? UnaryReduction(IUnaryOperation unary) =>
        IntegerKindOf(unary.Type) switch
        {
            null => null,
            { } kind when kind.IsSixtyFourBit() => SixtyFourBitUnaryReduction(unary, kind),
            { } kind => NarrowIntegerOverflow.UnaryReduction(
                unary.OperatorKind,
                kind,
                unary.IsChecked,
                IntegerValueRanges.Of(unary.Operand)
            ),
        };

    /// <summary>
    /// <c>&lt;&lt;</c> drops the bits shifted past bit 63 in either context
    /// (shifts never check, FR-022). <c>&gt;&gt;</c> and <c>&gt;&gt;&gt;</c>
    /// cannot leave the domain, so they are never reduced.
    /// </summary>
    private static IntegerOverflowReduction? SixtyFourBitBinaryReduction(
        BinaryOperatorKind operatorKind,
        IrNumericKind kind,
        bool isChecked
    ) =>
        operatorKind switch
        {
            BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply =>
                IntegerOverflowReduction.Of(kind, isChecked),
            BinaryOperatorKind.LeftShift => IntegerOverflowReduction.Of(kind, isChecked: false),
            _ => null,
        };

    /// <summary>
    /// <c>~u</c> on a <c>ulong</c> is negative as a <c>bigint</c>, so it wraps
    /// back into the unsigned domain; it cannot overflow, so it never checks.
    /// <c>~l</c> on a <c>long</c> stays in its domain and is not reduced.
    /// </summary>
    private static IntegerOverflowReduction? SixtyFourBitUnaryReduction(
        IUnaryOperation unary,
        IrNumericKind kind
    ) =>
        unary.OperatorKind switch
        {
            UnaryOperatorKind.Minus => IntegerOverflowReduction.Of(kind, unary.IsChecked),
            UnaryOperatorKind.BitwiseNegation when kind is IrNumericKind.UInt64 =>
                IntegerOverflowReduction.Of(kind, isChecked: false),
            _ => null,
        };

    /// <summary>The integer kind of <paramref name="type"/>, through <c>Nullable&lt;T&gt;</c>; <c>null</c> for an enum or a non-integer type.</summary>
    private static IrNumericKind? IntegerKindOf(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { IsEnum: false, Kind: var kind } && kind.IsInteger()
            ? kind
            : null;
}
