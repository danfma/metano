using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// A conversion lifted over <c>null</c> (spec 012 FR-019, baseline FR-079), the
/// one shape the numeric and the user-defined conversions share:
/// <c>x == null ? null : convert(x)</c> for an operand that is safe to read
/// twice, <c>mapNullable(x, (value) =&gt; convert(value))</c> otherwise, so the
/// operand is evaluated once. The loose <c>==</c> also catches the
/// <c>undefined</c> of an optional field.
/// </summary>
internal static class NullLiftedConversion
{
    /// <summary>The <c>metano-runtime</c> helper that maps a nullable value once.</summary>
    public const string MapNullable = "mapNullable";

    private const string LiftedValueName = "value";

    /// <summary>
    /// Lifts <paramref name="convert"/> over the <paramref name="operand"/>,
    /// whose TypeScript form is <paramref name="mapped"/>.
    /// </summary>
    public static TsExpression Map(
        IrExpression operand,
        TsExpression mapped,
        Func<TsExpression, TsExpression> convert
    )
    {
        if (IrEvaluationTraits.IsSideEffectFree(operand))
        {
            return new TsConditionalExpression(
                new TsBinaryExpression(mapped, "==", new TsLiteral("null")),
                new TsLiteral("null"),
                convert(mapped)
            );
        }

        var lambda = new TsArrowFunction(
            [new TsParameter(LiftedValueName, null)],
            [new TsReturnStatement(convert(new TsIdentifier(LiftedValueName)))]
        );

        return RuntimeCall(MapNullable, mapped, lambda);
    }
}
