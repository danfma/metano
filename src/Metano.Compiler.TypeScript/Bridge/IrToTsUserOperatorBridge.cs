using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using static Metano.Compiler.TypeScript.Bridge.Int64TsExpressions;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Renders the applications of user-defined operators and conversions
/// (baseline FR-077, FR-079) as calls of the static helpers their type
/// declares (FR-078): <c>Money.__add(a, b)</c>, the fast path of a dispatched
/// group (<c>Vec.__multiplyDoubleVec(2, v)</c>) or, for a type this
/// compilation reads only from metadata, its dispatcher, and
/// <c>Money.__implicitFromInt32(7)</c>. A C# 14 extension operator calls the
/// function its class's module exports, qualified by the block's receiver
/// (<c>__add$Sig(a, b)</c>, #761). A lifted <c>==</c> / <c>!=</c> and a
/// lifted conversion handle <c>null</c> here, evaluating each operand once.
/// </summary>
internal static class IrToTsUserOperatorBridge
{
    private const string LeftName = "left$lifted";
    private const string RightName = "right$lifted";

    private static readonly IrLiteral NullLiteral = new(null, IrLiteralKind.Null);

    public static TsExpression MapApplication(
        IrUserOperatorApplication application,
        IrToTsBridgeContext context
    ) =>
        application.IsLifted
            ? IrToTsExpressionBridge.Map(LiftedEquality(application), context)
            : HelperCall(
                application.Operator,
                application
                    .Operands.Select(operand => IrToTsExpressionBridge.Map(operand, context))
                    .ToList(),
                context
            );

    public static TsExpression MapConversion(
        IrUserConversion conversion,
        IrToTsBridgeContext context
    )
    {
        var operand = IrToTsExpressionBridge.Map(conversion.Operand, context);

        if (!conversion.IsLifted)
            return Converted(conversion, operand, context);

        return NullLiftedConversion.Map(conversion.Operand, operand, value =>
            Converted(conversion, value, context)
        );
    }

    /// <summary>
    /// The TypeScript name of the helper <paramref name="user"/> calls: the
    /// conversion's static, the claimed fast path of a dispatched overload,
    /// else the operator's helper or its group's dispatcher.
    /// </summary>
    public static string HelperNameOf(IrUserOperator user)
    {
        if (user.ConversionName is { } conversion)
            return TsOperatorNaming.ConversionHelperName(conversion);

        if (
            user.Overload is { } overload
            && TsOverloadCallSites.MethodFastPathOf(overload) is { } fastPath
        )
            return fastPath;

        var name = user.Pin
            ?? TsOperatorNaming.KindNameOf(user.Kind)
            ?? throw new InvalidOperationException(
                $"The operator '{user.MetadataName}' has no TypeScript helper."
            );

        return user.ExtensionReceiver is { } receiver
            ? TsOperatorNaming.ExtensionOperatorName(name, receiver)
            : TsOperatorNaming.StaticHelperName(name);
    }

    private static TsExpression Converted(
        IrUserConversion conversion,
        TsExpression operand,
        IrToTsBridgeContext context
    )
    {
        var called = HelperCall(conversion.Operator, [operand], context);

        if (conversion.After is not { } after)
            return called;

        return IrToTsNumericConversionBridge.Map(
            new IrNumericConversion(conversion, after.From, after.To, after.IsChecked),
            called
        );
    }

    private static TsCallExpression HelperCall(
        IrUserOperator user,
        IReadOnlyList<TsExpression> arguments,
        IrToTsBridgeContext context
    ) => new(HelperReference(user, context), arguments);

    /// <summary>
    /// The helper of a class's operator (<c>Money.__add</c>), or the function
    /// of an extension operator, which its class's module exports (#761).
    /// </summary>
    private static TsExpression HelperReference(IrUserOperator user, IrToTsBridgeContext context)
    {
        if (user.ExtensionReceiver is not null)
            return IrToTsExpressionBridge.ModuleExportReference(
                HelperNameOf(user),
                user.DeclaringType
            );

        return new TsPropertyAccess(
            IrToTsExpressionBridge.Map(user.DeclaringType, context),
            HelperNameOf(user)
        );
    }

    /// <summary>
    /// A lifted <c>==</c> is <c>true</c> when both operands are <c>null</c>,
    /// <c>false</c> when one is, and the operator's result otherwise
    /// (<c>l == null ? r == null : r != null &amp;&amp; T.__equals(l, r)</c>);
    /// <c>!=</c> is its negation. Each operand with a side effect is bound
    /// once.
    /// </summary>
    private static IrExpression LiftedEquality(IrUserOperatorApplication application)
    {
        var isNegated = application.BinaryOp is IrBinaryOp.NotEqual;

        return Bound(application.Operands[0], LeftName, left =>
            Bound(application.Operands[1], RightName, right =>
            {
                var applied = application with { Operands = [left, right], IsLifted = false };

                return new IrConditionalExpression(
                    Test(left, isNull: true),
                    Test(right, isNull: !isNegated),
                    new IrBinaryExpression(
                        Test(right, isNull: isNegated),
                        isNegated ? IrBinaryOp.LogicalOr : IrBinaryOp.LogicalAnd,
                        applied
                    )
                );
            })
        );
    }

    private static IrExpression Bound(
        IrExpression value,
        string name,
        Func<IrExpression, IrExpression> build
    ) =>
        IrEvaluationTraits.IsSideEffectFree(value)
            ? build(value)
            : new IrLetExpression(name, value, build(new IrIdentifier(name)));

    private static IrBinaryExpression Test(IrExpression value, bool isNull) =>
        new(value, isNull ? IrBinaryOp.Equal : IrBinaryOp.NotEqual, NullLiteral);
}
