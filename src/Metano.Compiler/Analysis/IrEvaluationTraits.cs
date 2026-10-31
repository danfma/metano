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
    /// <see cref="IsReusableReceiver"/>, also accepting null-conditional
    /// accesses over such shapes (<c>o?.Next</c>): a guard over them may
    /// repeat the receiver instead of binding it
    /// (<c>o?.next == null ? null : f(o?.next)</c>). Kept apart from
    /// <see cref="IsReusableReceiver"/> because a write through a repeated
    /// optional link is not a valid assignment target.
    /// </summary>
    public static bool IsReusableNullConditionalReceiver(IrExpression expression) =>
        expression switch
        {
            IrNullConditionalAccess access => IsReusableNullConditionalReceiver(access.Receiver)
                && IsReusableNullConditionalReceiver(access.WhenNotNull),
            IrMemberAccess access => IsReusableNullConditionalReceiver(access.Target),
            _ => IsReusableReceiver(expression),
        };

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
