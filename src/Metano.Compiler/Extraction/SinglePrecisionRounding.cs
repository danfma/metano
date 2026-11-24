using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The single-precision rounding of a <c>float</c> result (#695), for a
/// target whose <see cref="NumericLoweringRules.RoundsSinglePrecision"/> holds
/// a <c>float</c> in its 64-bit binary type: a built-in <c>+</c>, <c>-</c>,
/// <c>*</c> or <c>/</c> whose result is a <c>float</c>, the same compound
/// assignments, and <c>++</c> / <c>--</c> (lifted forms included) are rounded to single
/// precision, as the conversion of their <c>double</c> result to
/// <c>float</c>. The double result of these operators on two
/// <c>float</c> values, rounded once more, is the single-precision result,
/// since a double holds more than twice the 24 bits of a <c>float</c>
/// significand plus two. The <c>Sum</c> and <c>Average</c> of a LINQ chain
/// over <c>float</c> values round their <c>double</c> accumulation the same
/// way (<see cref="RoundsAggregate"/>). A <c>float</c> <c>%</c> (and <c>%=</c>, whose
/// right operand C# requires to convert implicitly to <c>float</c>) and a
/// unary <c>-</c> are exact, so they are left unchanged.
/// </summary>
internal static class SinglePrecisionRounding
{
    private static readonly IrNumericEndpoint DoubleEndpoint = new(IrNumericKind.Double);
    private static readonly IrNumericEndpoint SingleEndpoint = new(IrNumericKind.Single);

    /// <summary>Whether the result of <paramref name="operation"/> is rounded to single precision on this target.</summary>
    public static bool RoundsResultOf(IOperation? operation, NumericLoweringRules rules) =>
        rules.RoundsSinglePrecision
        && operation switch
        {
            IBinaryOperation { OperatorMethod: null } binary => IsSingle(binary.Type)
                && IsInexact(binary.OperatorKind),
            ICompoundAssignmentOperation { OperatorMethod: null } compound => IsSingle(
                compound.Type
            ) && IsInexact(compound.OperatorKind),
            IIncrementOrDecrementOperation { OperatorMethod: null } increment => IsSingle(
                increment.Type
            ),
            _ => false,
        };

    /// <summary>
    /// Whether a LINQ chain ending in <paramref name="terminal"/> rounds its
    /// <c>float</c> result on this target: .NET's <c>Sum</c> and
    /// <c>Average</c> over <c>float</c> values accumulate in <c>double</c>
    /// and convert the result to <c>float</c>. A <c>float?</c> result keeps
    /// its <c>null</c> (not rounded, a documented gap).
    /// </summary>
    public static bool RoundsAggregate(
        IrLinqOperator terminal,
        ITypeSymbol? resultType,
        NumericLoweringRules rules
    ) =>
        rules.RoundsSinglePrecision
        && terminal is IrLinqOperator.Sum or IrLinqOperator.Average
        && resultType?.SpecialType is SpecialType.System_Single;

    /// <summary><paramref name="value"/>, a <c>double</c> result, rounded to a <c>float</c>.</summary>
    public static IrNumericConversion Round(IrExpression value) =>
        new(value, DoubleEndpoint, SingleEndpoint, IsChecked: false);

    private static bool IsInexact(BinaryOperatorKind operatorKind) =>
        operatorKind
            is BinaryOperatorKind.Add
                or BinaryOperatorKind.Subtract
                or BinaryOperatorKind.Multiply
                or BinaryOperatorKind.Divide;

    private static bool IsSingle(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { Kind: IrNumericKind.Single, IsEnum: false };
}
