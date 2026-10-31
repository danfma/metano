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
            IrBaseExpression => true,
            IrTypeReference => true,
            IrMemberAccess access => IsReusableReceiver(access.Target),
            _ => false,
        };

    /// <summary>
    /// <see cref="IsReusableReceiver"/>, also accepting null-conditional links
    /// over such shapes (<c>o?.Next</c>): a guard over them may repeat the
    /// receiver instead of binding it (<c>o?.next == null ? null : f(o?.next)</c>).
    /// Kept apart from <see cref="IsReusableReceiver"/> because a write
    /// through a repeated optional link is not a valid assignment target.
    /// </summary>
    public static bool IsReusableNullConditionalReceiver(IrExpression expression) =>
        expression switch
        {
            IrNullConditionalAccess { IsBound: false } access => IsReusableNullConditionalReceiver(
                access.Receiver
            ) && IsReusableNullConditionalReceiver(access.WhenNotNull),
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
    /// How many times <paramref name="expression"/> reads the identifier
    /// <paramref name="name"/>, nested lambda bodies included.
    /// </summary>
    public static int CountIdentifierReads(IrExpression expression, string name) =>
        (expression is IrIdentifier identifier && identifier.Name == name ? 1 : 0)
        + IrNodeChildren.ExpressionsOf(expression).Sum(child => CountIdentifierReads(child, name))
        + IrNodeChildren
            .StatementsOf(expression)
            .Sum(statement => CountIdentifierReads(statement, name));

    private static int CountIdentifierReads(IrStatement statement, string name) =>
        IrNodeChildren.ExpressionsOf(statement).Sum(child => CountIdentifierReads(child, name))
        + IrNodeChildren.StatementsOf(statement).Sum(nested => CountIdentifierReads(nested, name));
}
