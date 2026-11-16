using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The LINQ <c>Sum</c>, <c>Average</c>, <c>Min</c>, <c>Max</c>, <c>MinBy</c>
/// and <c>MaxBy</c> terminals the generic <c>number</c> ones cannot be (#618,
/// #657): a <c>decimal</c> is a decimal.js object, so its sum and average
/// have their own terminals (the sum takes the <c>Decimal</c> class to build
/// its zero), and a result that .NET gives <c>null</c> for an empty sequence
/// (<c>int?</c>, <c>string</c>, a class) has a nullable terminal, where the
/// plain one throws as .NET does. <c>Min</c> and <c>Max</c> of decimals are
/// the generic terminals, which order a decimal by value.
/// </summary>
internal static class LinqAggregateTerminals
{
    private const string NullableSuffix = "Nullable";

    /// <summary>The pipe helper the stage calls.</summary>
    public static string EmittedName(IrLinqStage stage) =>
        (stage.Operator, stage.AggregateValues) switch
        {
            (IrLinqOperator.Sum, { IsDecimal: true }) => "sumDecimal",
            (IrLinqOperator.Average, { IsDecimal: true, EmptyIsNull: true }) =>
                "averageNullableDecimal",
            (IrLinqOperator.Average, { IsDecimal: true }) => "averageDecimal",
            (_, { EmptyIsNull: true }) when HasNullableVariant(stage.Operator) =>
                IrLinqMapping.EmittedName(stage.Operator) + NullableSuffix,
            _ => IrLinqMapping.EmittedName(stage.Operator),
        };

    /// <summary>
    /// The arguments the helper takes before the stage's own: the
    /// <c>Decimal</c> class a decimal sum builds its zero from.
    /// </summary>
    public static IEnumerable<TsExpression> LeadingArguments(IrLinqStage stage) =>
        stage is { Operator: IrLinqOperator.Sum, AggregateValues.IsDecimal: true }
            ? [IrToTsExpressionBridge.BclDecimalValueReference()]
            : [];

    private static bool HasNullableVariant(IrLinqOperator op) =>
        op
            is IrLinqOperator.Average
                or IrLinqOperator.Min
                or IrLinqOperator.Max
                or IrLinqOperator.MinBy
                or IrLinqOperator.MaxBy;
}
