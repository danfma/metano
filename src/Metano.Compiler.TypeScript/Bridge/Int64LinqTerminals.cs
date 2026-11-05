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

        return stage.Operator switch
        {
            IrLinqOperator.Sum => "sumInt64",
            IrLinqOperator.Average => values.IsNullable ? "averageNullableInt64" : "averageInt64",
            IrLinqOperator.Min => ExtremumName("min", values),
            IrLinqOperator.Max => ExtremumName("max", values),
            _ => IrLinqMapping.EmittedName(stage.Operator),
        };
    }

    /// <summary>Whether the chain ends in a <c>LongCount</c>, whose <c>number</c> count becomes a <c>bigint</c>.</summary>
    public static bool CountsInInt64(IrLinqChain chain) =>
        Int64RepresentationScope.IsBigInt
        && chain.Stages is [.., { Operator: IrLinqOperator.Count, SixtyFourBitValues: not null }];

    private static string ExtremumName(string extremum, IrLinqSixtyFourBitValues values)
    {
        var type = (values.IsNullable, values.Kind) switch
        {
            (true, _) => "NullableInt64",
            (false, IrNumericKind.UInt64) => "UInt64",
            _ => "Int64",
        };

        return $"{extremum}{type}Of";
    }
}
