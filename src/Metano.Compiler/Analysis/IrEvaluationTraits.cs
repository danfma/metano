using Metano.Compiler.IR;

namespace Metano.Compiler.Analysis;

/// <summary>
/// Evaluation-order facts about an IR expression that lowerings consult before
/// duplicating or deferring it.
/// </summary>
public static class IrEvaluationTraits
{
    /// <summary>
    /// True for IR shapes that can be referenced twice in the generated code
    /// without observable side effects: identifiers, <c>this</c>, and
    /// member-access chains rooted at one of those (fields and properties
    /// alike — the IR does not distinguish them, so a property getter is
    /// assumed side-effect free). Method calls, element accesses and every
    /// other shape are excluded — re-evaluating them would diverge from the
    /// C# semantics where the receiver is evaluated exactly once.
    /// </summary>
    /// <remarks>Property getters are assumed side-effect free.</remarks>
    public static bool IsReusableReceiver(IrExpression expression) =>
        expression switch
        {
            IrIdentifier => true,
            IrThisExpression => true,
            IrConditionalReceiver => true,
            IrBaseExpression => true,
            IrTypeReference => true,
            IrMemberAccess access => IsReusableReceiver(access.Target),
            _ => false,
        };

    /// <summary>
    /// Shape-based: literals, <see cref="IsReusableReceiver">reusable
    /// receivers</see>, and non-assigning operators over those. Property
    /// getters are assumed side-effect free, as in
    /// <see cref="IsReusableReceiver"/>.
    /// </summary>
    public static bool IsSideEffectFree(IrExpression expression) =>
        expression switch
        {
            IrLiteral => true,
            IrUnaryExpression
            {
                Operator: not (IrUnaryOp.Increment or IrUnaryOp.Decrement)
            } unary => IsSideEffectFree(unary.Operand),
            IrBinaryExpression binary when !binary.Operator.IsAssignment() => IsSideEffectFree(
                binary.Left
            ) && IsSideEffectFree(binary.Right),
            _ => IsReusableReceiver(expression),
        };

    /// <summary>
    /// Whether a null-conditional guard may repeat this receiver textually
    /// instead of binding it: a <see cref="IsReusableReceiver">reusable
    /// receiver</see> that reads no <see cref="IrConditionalReceiver"/>.
    /// A receiver that reads an enclosing access's placeholder
    /// (<c>$receiver.next</c> inside another guard) or is itself a
    /// null-conditional access is always bound, so the bound value is
    /// computed as the guard's argument, in the enclosing scope, and never
    /// re-read under a guard that rebinds the same parameter name.
    /// </summary>
    public static bool IsReusableNullConditionalReceiver(IrExpression expression) =>
        IsReusableReceiver(expression) && CountConditionalReceiverReads(expression) == 0;

    /// <summary>
    /// Whether the expression awaits outside any nested lambda — a lambda's
    /// own <c>await</c> belongs to that lambda, not to the enclosing
    /// expression.
    /// </summary>
    public static bool ContainsAwait(IrExpression expression) =>
        expression is IrAwaitExpression
        || IrNodeChildren.ExpressionsOf(expression).Any(ContainsAwait);

    /// <summary>
    /// How many times <paramref name="whenNotNull"/> reads its own
    /// <see cref="IrConditionalReceiver"/>: placeholders inside a nested
    /// access's <see cref="IrNullConditionalAccess.WhenNotNull"/> belong to
    /// that access and are not counted.
    /// </summary>
    public static int CountConditionalReceiverReads(IrExpression whenNotNull) =>
        whenNotNull switch
        {
            IrConditionalReceiver => 1,
            IrNullConditionalAccess nested => CountConditionalReceiverReads(nested.Receiver),
            _ => IrNodeChildren.ExpressionsOf(whenNotNull).Sum(CountConditionalReceiverReads)
                + IrNodeChildren.StatementsOf(whenNotNull).Sum(CountConditionalReceiverReads),
        };

    private static int CountConditionalReceiverReads(IrStatement statement) =>
        IrNodeChildren.ExpressionsOf(statement).Sum(CountConditionalReceiverReads)
        + IrNodeChildren.StatementsOf(statement).Sum(CountConditionalReceiverReads);
}
