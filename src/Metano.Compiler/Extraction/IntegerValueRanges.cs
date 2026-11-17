using System.Runtime.CompilerServices;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <see cref="IntegerRange"/> of the values a Roslyn operation can
/// produce, read from what Roslyn already knows: a constant is its value, a
/// conversion that cannot change the value keeps its operand's range, a
/// built-in <c>+</c>, <c>-</c>, <c>*</c>, <c>&amp;</c>, <c>|</c>, <c>^</c>
/// or <c>&lt;&lt;</c> by a constant combines its operands' ranges, a
/// <c>char</c> is its UTF-16 code when the target lowers char codes, the
/// <c>Length</c> of an array or a <c>string</c> and the <c>Count</c> of a
/// BCL collection that owns its storage are non-negative when the target
/// reads those ranges (spec 001 FR-075; <see cref="NumericLoweringRules"/>
/// decides both), and anything else spans its type. It is not a data-flow
/// analysis: a variable spans its type.
/// </summary>
internal static class IntegerValueRanges
{
    /// <summary>
    /// The BCL collection classes whose <c>Count</c> is the number of
    /// elements they store (never a value a user type returns).
    /// </summary>
    private static readonly string[] StorageOwningCollections =
    [
        IrExpressionExtractor.ListTypeName,
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Queue`1",
        "System.Collections.Generic.Stack`1",
        "System.Collections.Generic.SortedDictionary`2",
        "System.Collections.Generic.SortedList`2",
        "System.Collections.Generic.SortedSet`1",
        "System.Collections.Generic.LinkedList`1",
    ];

    /// <summary><see cref="StorageOwningCollections"/> resolved once per compilation.</summary>
    private static readonly ConditionalWeakTable<
        Compilation,
        HashSet<INamedTypeSymbol>
    > ResolvedCollections = new();

    /// <summary>
    /// The values <paramref name="operation"/> can produce; <c>null</c> when
    /// its type is not an integer of 64 bits or fewer (an enum reads as its
    /// underlying kind), as the target's <paramref name="rules"/> read it.
    /// </summary>
    public static IntegerRange? Of(IOperation? operation, NumericLoweringRules rules)
    {
        if (operation is null)
            return null;

        if (ConstantOf(operation, rules) is { } constant)
            return IntegerRange.Exactly(constant);

        if (OfType(operation.Type, rules) is not { } typeRange)
            return null;

        var exact = operation switch
        {
            IConversionOperation { Conversion.IsUserDefined: false } conversion => Of(
                conversion.Operand,
                rules
            ),
            IBinaryOperation { OperatorMethod: null } binary => ExactResult(
                binary.OperatorKind,
                Of(binary.LeftOperand, rules),
                Of(binary.RightOperand, rules)
            ),
            IPropertyReferenceOperation property
                when rules.ReadsCollectionAndLoopRanges && IsStoredElementCount(property) =>
                IntegerRange.NonNegativeOf(IrNumericKind.Int32),
            _ => null,
        };

        // A value outside the type's range wraps (or throws), so it says
        // nothing beyond the type itself.
        return exact is { } range && typeRange.Contains(range) ? range : typeRange;
    }

    /// <summary>
    /// The values of the integer type <paramref name="type"/>, through
    /// <c>Nullable&lt;T&gt;</c>, or of a <c>char</c> when the target lowers
    /// char codes; <c>null</c> for any other type.
    /// </summary>
    public static IntegerRange? OfType(ITypeSymbol? type, NumericLoweringRules rules) =>
        NumericEndpoints.Of(type) is { Kind: var kind } && IsRangedKind(kind, rules)
            ? IntegerRange.Of(kind)
            : null;

    private static bool IsRangedKind(IrNumericKind kind, NumericLoweringRules rules) =>
        kind.IsInteger() || (rules.LowersCharCodes && kind is IrNumericKind.Char);

    /// <summary>
    /// The <c>Length</c> of an array or a <c>string</c>, or the <c>Count</c>
    /// of one of <see cref="StorageOwningCollections"/>, matched by symbol
    /// identity.
    /// </summary>
    private static bool IsStoredElementCount(IPropertyReferenceOperation property) =>
        property.Property switch
        {
            {
                Name: "Length",
                ContainingType.SpecialType: SpecialType.System_Array or SpecialType.System_String,
            } => true,
            { Name: "Count", ContainingType.OriginalDefinition: var owner } => property
                .SemanticModel
                ?.Compilation
                is { } compilation
                && ResolvedCollections.GetValue(compilation, Resolve).Contains(owner),
            _ => false,
        };

    private static HashSet<INamedTypeSymbol> Resolve(Compilation compilation) =>
        StorageOwningCollections
            .Select(compilation.GetTypeByMetadataName)
            .OfType<INamedTypeSymbol>()
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    /// <summary>
    /// The mathematical result of the built-in operator <paramref name="kind"/>
    /// over operands in <paramref name="left"/> and <paramref name="right"/>,
    /// before any wrap; <c>null</c> when an operand range is unknown, the
    /// operator is not one this reads, or its bound cannot be stated. A left
    /// shift is read for a constant count only.
    /// </summary>
    public static IntegerRange? ExactResult(
        BinaryOperatorKind kind,
        IntegerRange? left,
        IntegerRange? right
    )
    {
        if (left is not { } l || right is not { } r)
            return null;

        return kind switch
        {
            BinaryOperatorKind.Add => l.Plus(r),
            BinaryOperatorKind.Subtract => l.Minus(r),
            BinaryOperatorKind.Multiply => l.Times(r),
            BinaryOperatorKind.And => l.BitwiseAnd(r),
            BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr => l.BitwiseUnion(r),
            BinaryOperatorKind.LeftShift when r.Min == r.Max => l.ShiftedLeft(ConstantCountOf(r)),
            _ => null,
        };
    }

    /// <summary>A constant shift count as an <c>int</c>; <c>-1</c> (no valid count) when it does not fit one.</summary>
    private static int ConstantCountOf(IntegerRange count) =>
        count.Min >= 0 && count.Min <= int.MaxValue ? (int)count.Min : -1;

    private static Int128? ConstantOf(IOperation operation, NumericLoweringRules rules) =>
        operation.ConstantValue is { HasValue: true, Value: var value }
            ? value switch
            {
                sbyte v => v,
                byte v => v,
                short v => v,
                ushort v => v,
                int v => v,
                uint v => v,
                long v => v,
                ulong v => v,
                char v when rules.LowersCharCodes => v,
                _ => null,
            }
            : null;
}
