using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

public static partial class IrToTsExpressionBridge
{
    private static readonly TsLiteral NullLiteral = new("null");

    /// <summary>
    /// The parameter a receiver that must evaluate once is bound to. Nested
    /// guards reuse the name safely by a local rule: a receiver that reads
    /// an enclosing access's placeholder is never repeated textually
    /// (<see cref="IrEvaluationTraits.IsReusableNullConditionalReceiver"/>),
    /// it is bound, and a bound value is the guard's argument — evaluated in
    /// the enclosing scope, where <c>$receiver</c> still names the enclosing
    /// receiver. Inside the guard, every placeholder belongs to the guard
    /// itself. <c>$</c> cannot appear in a C# identifier, so the name never
    /// captures a source local.
    /// </summary>
    private const string ConditionalReceiverParameter = "$receiver";

    /// <summary>
    /// What each <see cref="IrConditionalReceiver"/> lowers to, innermost
    /// access on top, while that access's <c>WhenNotNull</c> is mapped.
    /// </summary>
    [ThreadStatic]
    private static Stack<TsExpression>? _conditionalReceivers;

    /// <summary>
    /// Lowers a null-conditional access (<c>a?.B.C(x)</c>). When every mapped
    /// step is a plain property access, element access or call on the
    /// receiver, the result is native optional chaining (<c>a?.b.c(x)</c>).
    /// Otherwise — a step mapped to a template, a wrapped receiver, an
    /// extension, <c>[PlainObject]</c> or LINQ helper that takes the receiver
    /// as an argument — <c>?.</c> cannot carry the short-circuit, so the
    /// receiver is guarded explicitly: <c>a == null ? null : step(a)</c>, or,
    /// for a receiver that must evaluate once,
    /// <c>((t) =&gt; t == null ? null : step(t))(a)</c>.
    /// </summary>
    private static TsExpression MapNullConditionalAccess(
        IrNullConditionalAccess access,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var lowered = LowerNullConditional(access, bclRegistry);

        if (lowered.OptionalChain is { } chain)
            return chain;

        if (lowered.ReceiverName is not { } receiverName)
            return GuardInline(lowered.Receiver, lowered.WhenNotNull);

        var guarded = GuardInline(new TsIdentifier(receiverName), lowered.WhenNotNull);

        return TsImmediateInvocation.Of(
            [new TsImmediateBinding(receiverName, lowered.Receiver)],
            [new TsReturnStatement(guarded)],
            isAsync: IrEvaluationTraits.ContainsAwait(access.WhenNotNull)
        );
    }

    /// <summary>
    /// A null-conditional access in statement position (its value unused):
    /// native optional chaining when it applies, otherwise
    /// <c>if (a != null) step(a);</c>, with a receiver that must evaluate
    /// once bound first (<c>{ const t = a; if (t != null) step(t); }</c>).
    /// </summary>
    public static TsStatement MapNullConditionalStatement(
        IrNullConditionalAccess access,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var lowered = LowerNullConditional(access, bclRegistry);

        if (lowered.OptionalChain is { } chain)
            return new TsExpressionStatement(chain);

        if (lowered.ReceiverName is not { } receiverName)
            return RunWhenNotNull(lowered.Receiver, lowered.WhenNotNull);

        return new TsBlockStatement([
            new TsVariableDeclaration(receiverName, lowered.Receiver),
            RunWhenNotNull(new TsIdentifier(receiverName), lowered.WhenNotNull),
        ]);
    }

    /// <summary>
    /// Decides how the receiver is evaluated once: a side-effect-free
    /// receiver (<see cref="IrEvaluationTraits.IsReusableNullConditionalReceiver"/>)
    /// is repeated wherever the steps read it; any other receiver is read
    /// through <see cref="ConditionalReceiverParameter"/>. Either way the steps
    /// become native optional chaining when they read the receiver only at
    /// the chain's root (or anywhere, for a repeated receiver) and every
    /// mapped step is a plain access or call.
    /// </summary>
    private static LoweredNullConditional LowerNullConditional(
        IrNullConditionalAccess access,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var loweredReceiver = Map(access.Receiver, bclRegistry);
        var repeatsReceiver = IrEvaluationTraits.IsReusableNullConditionalReceiver(access.Receiver);

        var receiverReference = repeatsReceiver
            ? loweredReceiver
            : new TsIdentifier(ConditionalReceiverParameter);

        var whenNotNull = MapWithConditionalReceiver(
            access.WhenNotNull,
            receiverReference,
            bclRegistry
        );

        var optionalChain =
            whenNotNull.Equals(receiverReference)
                // A step that lowers to the receiver itself (a `[Branded]` value
                // read, `slug?.Value`) needs no guard: a null receiver is null.
                ? loweredReceiver
            : CanChainOptionally(access, repeatsReceiver)
                ? TryChainOptionally(whenNotNull, receiverReference, loweredReceiver)
            : null;

        return new LoweredNullConditional(
            loweredReceiver,
            repeatsReceiver ? null : ConditionalReceiverParameter,
            whenNotNull,
            optionalChain
        );
    }

    /// <summary>
    /// Native <c>?.</c> replaces a bound receiver only at the chain's root,
    /// so a receiver read anywhere else too — an index-from-end offset
    /// (<c>f()?[^1]</c>), a slice bound, an <c>[Inline]</c> body that
    /// mentions it twice — keeps the binding. A repeated receiver may appear
    /// anywhere.
    /// </summary>
    private static bool CanChainOptionally(IrNullConditionalAccess access, bool repeatsReceiver) =>
        repeatsReceiver
        || IrEvaluationTraits.CountConditionalReceiverReads(access.WhenNotNull) == 1;

    private static TsExpression MapWithConditionalReceiver(
        IrExpression whenNotNull,
        TsExpression receiverReference,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var receivers = _conditionalReceivers ??= new Stack<TsExpression>();
        receivers.Push(receiverReference);

        try
        {
            return Map(whenNotNull, bclRegistry);
        }
        finally
        {
            receivers.Pop();
        }
    }

    private static TsExpression CurrentConditionalReceiver() =>
        _conditionalReceivers is { Count: > 0 } receivers
            ? receivers.Peek()
            : throw new InvalidOperationException(
                "A conditional receiver was read outside its null-conditional access."
            );

    private static TsConditionalExpression GuardInline(
        TsExpression receiver,
        TsExpression whenNotNull
    ) => new(new TsBinaryExpression(receiver, "==", NullLiteral), NullLiteral, whenNotNull);

    private static TsIfStatement RunWhenNotNull(TsExpression receiver, TsExpression whenNotNull) =>
        new(
            new TsBinaryExpression(receiver, "!=", NullLiteral),
            [new TsExpressionStatement(whenNotNull)]
        );

    /// <summary>
    /// Rebuilds <paramref name="step"/> as an optional chain rooted at
    /// <paramref name="loweredReceiver"/>: the step applied directly to
    /// <paramref name="receiverReference"/> becomes the <c>?.</c> link and
    /// every step above it continues the chain. Returns <c>null</c> when the
    /// spine leaves plain property/element accesses and calls before
    /// reaching the receiver.
    /// </summary>
    private static TsExpression? TryChainOptionally(
        TsExpression step,
        TsExpression receiverReference,
        TsExpression loweredReceiver
    )
    {
        if (ChainObjectOf(step) is not { } chainObject)
            return null;

        if (chainObject.Equals(receiverReference))
            return WithChainObject(step, loweredReceiver, isLink: true);

        return TryChainOptionally(chainObject, receiverReference, loweredReceiver) is { } chained
            ? WithChainObject(step, chained, isLink: false)
            : null;
    }

    /// <summary>The object a property/element access or call is applied to.</summary>
    private static TsExpression? ChainObjectOf(TsExpression step) =>
        step switch
        {
            TsPropertyAccess access => access.Object,
            TsElementAccess element => element.Object,
            TsCallExpression call => call.Callee,
            _ => null,
        };

    /// <summary>
    /// <paramref name="step"/> applied to <paramref name="chainObject"/>,
    /// either as the chain's <c>?.</c> link or as a step continuing it (a
    /// call needs no marker: its callee never closes the chain).
    /// </summary>
    private static TsExpression WithChainObject(
        TsExpression step,
        TsExpression chainObject,
        bool isLink
    ) =>
        step switch
        {
            TsPropertyAccess access => access with
            {
                Object = chainObject,
                Optional = isLink || access.Optional,
                ContinuesChain = !isLink,
            },
            TsElementAccess element => element with
            {
                Object = chainObject,
                Optional = isLink || element.Optional,
                ContinuesChain = !isLink,
            },
            TsCallExpression call => call with
            {
                Callee = chainObject,
                Optional = isLink || call.Optional,
            },
            _ => throw new InvalidOperationException($"Not a chain step: {step.GetType().Name}"),
        };

    /// <summary>
    /// A lowered null-conditional access: the receiver, its binding name when
    /// it must evaluate once, the non-null branch, and the native optional
    /// chain when every step allows one.
    /// </summary>
    private sealed record LoweredNullConditional(
        TsExpression Receiver,
        string? ReceiverName,
        TsExpression WhenNotNull,
        TsExpression? OptionalChain
    );
}
