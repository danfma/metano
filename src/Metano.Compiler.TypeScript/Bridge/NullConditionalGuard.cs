using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers C#'s null-conditional short-circuit to TypeScript when native
/// optional chaining cannot express the mapped step (a template, a wrapped
/// receiver, a helper call that takes the receiver as an argument): the
/// receiver is evaluated once, the result is <c>null</c> when it is null, and
/// the mapped step (arguments included) runs only otherwise.
/// </summary>
internal static class NullConditionalGuard
{
    // `$` cannot appear in a C# identifier, so this parameter cannot capture
    // a source local referenced by the guarded step.
    private const string ReceiverParameterName = "$__metanoOptionalReceiver";

    /// <summary>
    /// Guards <paramref name="receiver"/> (lowered from
    /// <paramref name="receiverSource"/>) and builds the non-null branch over
    /// it. A side-effect-free receiver is repeated in place
    /// (<c>recv == null ? null : step(recv)</c>); any other receiver is bound
    /// once through an arrow parameter.
    /// </summary>
    public static TsExpression Guard(
        IrExpression receiverSource,
        TsExpression receiver,
        bool awaitsWhenNotNull,
        Func<TsExpression, TsExpression> buildWhenNotNull
    )
    {
        if (IrEvaluationTraits.IsReusableReceiver(receiverSource))
            return Inline(receiver, buildWhenNotNull(receiver));

        var boundReceiver = new TsIdentifier(ReceiverParameterName);

        return Bound(
            ReceiverParameterName,
            receiver,
            buildWhenNotNull(boundReceiver),
            awaitsWhenNotNull
        );
    }

    /// <summary><c>receiver == null ? null : whenNotNull</c>.</summary>
    public static TsExpression Inline(TsExpression receiver, TsExpression whenNotNull) =>
        new TsConditionalExpression(
            new TsBinaryExpression(receiver, "==", new TsLiteral("null")),
            new TsLiteral("null"),
            whenNotNull
        );

    /// <summary>
    /// <c>((name) =&gt; name == null ? null : whenNotNull)(receiver)</c>. The
    /// parameter carries no annotation, so TypeScript types it from the
    /// argument and narrows it in the non-null branch. A branch that awaits
    /// makes the arrow <c>async</c> and the call awaited, keeping the
    /// <c>await</c> inside an async function.
    /// </summary>
    public static TsExpression Bound(
        string parameterName,
        TsExpression receiver,
        TsExpression whenNotNull,
        bool isAsync
    )
    {
        var guarded = Inline(new TsIdentifier(parameterName), whenNotNull);

        var arrow = new TsArrowFunction(
            [new TsParameter(parameterName, null)],
            [new TsReturnStatement(guarded)],
            Async: isAsync
        );

        var call = new TsCallExpression(arrow, [receiver]);

        return isAsync ? new TsAwaitExpression(call) : call;
    }
}
