using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// Lowers a null-conditional continuation (<c>a?.B.C(x)</c>). When every
    /// mapped step is a plain property access, element access or call on the
    /// bound receiver, the result is native optional chaining
    /// (<c>a?.b.c(x)</c>). Otherwise — a step mapped to a template, a wrapped
    /// receiver, an extension or LINQ helper that takes the receiver as an
    /// argument — <c>?.</c> cannot carry the short-circuit, so the receiver
    /// is guarded explicitly (see <see cref="NullConditionalGuard"/>).
    /// </summary>
    private static TsExpression MapNullConditionalAccess(
        IrNullConditionalAccess access,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var receiver = Map(access.Receiver, bclRegistry);
        var boundReceiver = access.ReceiverName is { } name
            ? Map(new IrIdentifier(name), bclRegistry)
            : receiver;
        var whenNotNull = Map(access.WhenNotNull, bclRegistry);

        if (TryChainOptionally(whenNotNull, boundReceiver, receiver) is { } chain)
            return chain;

        if (
            boundReceiver is TsIdentifier { Name: var parameterName }
            && access.ReceiverName is not null
        )
        {
            return NullConditionalGuard.Bound(
                parameterName,
                receiver,
                whenNotNull,
                IrEvaluationTraits.ContainsAwait(access.WhenNotNull)
            );
        }

        return NullConditionalGuard.Inline(receiver, whenNotNull);
    }

    /// <summary>
    /// Rebuilds <paramref name="step"/> as an optional chain rooted at
    /// <paramref name="receiver"/>: the step applied directly to
    /// <paramref name="boundReceiver"/> becomes the <c>?.</c> link and every
    /// step above it continues the chain. Returns <c>null</c> when the spine
    /// leaves plain property/element accesses and calls before reaching the
    /// bound receiver.
    /// </summary>
    private static TsExpression? TryChainOptionally(
        TsExpression step,
        TsExpression boundReceiver,
        TsExpression receiver
    ) =>
        step switch
        {
            TsPropertyAccess access when access.Object.Equals(boundReceiver) => access with
            {
                Object = receiver,
                Optional = true,
            },
            TsPropertyAccess access => TryChainOptionally(access.Object, boundReceiver, receiver)
                is { } chained
                ? access with
                {
                    Object = chained,
                    ContinuesChain = true,
                }
                : null,
            TsElementAccess element when element.Object.Equals(boundReceiver) => element with
            {
                Object = receiver,
                Optional = true,
            },
            TsElementAccess element => TryChainOptionally(element.Object, boundReceiver, receiver)
                is { } chained
                ? element with
                {
                    Object = chained,
                    ContinuesChain = true,
                }
                : null,
            TsCallExpression call when call.Callee.Equals(boundReceiver) => call with
            {
                Callee = receiver,
                Optional = true,
            },
            TsCallExpression call => TryChainOptionally(call.Callee, boundReceiver, receiver)
                is { } chained
                ? call with
                {
                    Callee = chained,
                }
                : null,
            _ => null,
        };
}
