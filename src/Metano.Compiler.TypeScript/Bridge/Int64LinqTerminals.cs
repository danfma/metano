using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The LINQ terminals over 64-bit values (spec 012 FR-036, FR-041): a
/// <c>Sum</c>, <c>Average</c>, <c>Min</c> or <c>Max</c> of <c>bigint</c>
/// values calls the <c>metano-runtime/system/linq</c> terminal written for
/// them, and a <c>LongCount</c> converts the count to <c>bigint</c>.
/// </summary>
internal static class Int64LinqTerminals
{
    /// <summary>The pipe helper the stage calls.</summary>
    public static string EmittedName(IrLinqStage stage)
    {
        // STAGING(012k): every mode once the switch is gone.
        if (!Int64RepresentationScope.IsBigInt || stage.SixtyFourBitValues is not { } values)
            return IrLinqMapping.EmittedName(stage.Operator);

        return (stage.Operator, values.IsNullable, values.Kind) switch
        {
            (IrLinqOperator.Sum, _, _) => Int64RuntimeHelpers.SumInt64,
            (IrLinqOperator.Average, true, _) => Int64RuntimeHelpers.AverageNullableInt64,
            (IrLinqOperator.Average, false, _) => Int64RuntimeHelpers.AverageInt64,
            (IrLinqOperator.Min, true, _) => Int64RuntimeHelpers.MinNullableInt64Of,
            (IrLinqOperator.Min, false, IrNumericKind.UInt64) => Int64RuntimeHelpers.MinUInt64Of,
            (IrLinqOperator.Min, false, _) => Int64RuntimeHelpers.MinInt64Of,
            (IrLinqOperator.Max, true, _) => Int64RuntimeHelpers.MaxNullableInt64Of,
            (IrLinqOperator.Max, false, IrNumericKind.UInt64) => Int64RuntimeHelpers.MaxUInt64Of,
            (IrLinqOperator.Max, false, _) => Int64RuntimeHelpers.MaxInt64Of,
            _ => IrLinqMapping.EmittedName(stage.Operator),
        };
    }

    /// <summary>Whether the chain ends in a <c>LongCount</c>, whose <c>number</c> count becomes a <c>bigint</c>.</summary>
    public static bool EndsInLongCount(IrLinqChain chain) =>
        // STAGING(012k): every mode once the switch is gone.
        Int64RepresentationScope.IsBigInt
        && chain.Stages is [.., { Operator: IrLinqOperator.Count, SixtyFourBitValues: not null }];
}
