using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renders the operations bound at run time on a <c>dynamic</c> value (spec 011
/// FR-030). A member access, call or indexer of a <c>dynamic</c> receiver is the
/// plain JavaScript one, its member name verbatim; a numeric cast of a
/// <c>dynamic</c> value calls the <c>metano-runtime</c> helper that converts it
/// from its runtime value (<c>numberFromDynamic</c>, <c>charFromDynamic</c>,
/// <c>bigintFromDynamic</c>, and <c>decimalSourceFromDynamic</c> for
/// <c>new Decimal(…)</c>), lifted over
/// <c>null</c> for a <c>T?</c> target.
/// </summary>
internal static class IrToTsDynamicBridge
{
    public const string NumberHelper = "numberFromDynamic";
    public const string CharHelper = "charFromDynamic";
    public const string BigIntHelper = "bigintFromDynamic";
    public const string DecimalSourceHelper = "decimalSourceFromDynamic";

    public static TsExpression MapMemberAccess(
        IrDynamicMemberAccess access,
        IrToTsBridgeContext context
    ) =>
        new TsPropertyAccess(
            IrToTsExpressionBridge.Map(access.Receiver, context),
            access.MemberName
        );

    public static TsExpression MapInvocation(
        IrDynamicInvocation invocation,
        IrToTsBridgeContext context
    ) =>
        new TsCallExpression(
            IrToTsExpressionBridge.Map(invocation.Target, context),
            invocation
                .Arguments.Select(argument => IrToTsExpressionBridge.Map(argument, context))
                .ToList()
        );

    public static TsExpression MapIndexerAccess(
        IrDynamicIndexerAccess indexer,
        IrToTsBridgeContext context
    ) =>
        new TsElementAccess(
            IrToTsExpressionBridge.Map(indexer.Receiver, context),
            IrToTsExpressionBridge.Map(indexer.Index, context)
        );

    public static TsExpression MapNumericConversion(
        IrDynamicNumericConversion conversion,
        IrToTsBridgeContext context
    )
    {
        var operand = IrToTsExpressionBridge.Map(conversion.Operand, context);

        return conversion.IsLifted
            ? NullLiftedConversion.Map(conversion.Operand, operand, value =>
                Converted(conversion, value)
            )
            : Converted(conversion, operand);
    }

    private static TsExpression Converted(
        IrDynamicNumericConversion conversion,
        TsExpression value
    ) =>
        conversion.Target switch
        {
            IrNumericKind.Char => RuntimeCall(CharHelper, [value, .. CheckedFlag(conversion)]),
            IrNumericKind.Int64 or IrNumericKind.UInt64 or IrNumericKind.BigInteger => RuntimeCall(
                BigIntHelper,
                [value, KindLiteral(conversion.Target), .. CheckedFlag(conversion)]
            ),
            IrNumericKind.Decimal => new TsNewExpression(
                IrToTsExpressionBridge.BclDecimalValueReference(),
                [RuntimeCall(DecimalSourceHelper, value)]
            ),
            _ => RuntimeCall(
                NumberHelper,
                [value, KindLiteral(conversion.Target), .. CheckedFlag(conversion)]
            ),
        };

    private static TsLiteral KindLiteral(IrNumericKind kind) => new($"\"{kind}\"");

    /// <summary>The helper's optional <c>checked</c> argument, written only in a checked context.</summary>
    private static TsExpression[] CheckedFlag(IrDynamicNumericConversion conversion) =>
        conversion.IsChecked ? [new TsLiteral("true")] : [];
}
