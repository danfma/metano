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
    /// Whether the expression awaits outside any nested lambda — a lambda's
    /// own <c>await</c> belongs to that lambda, not to the enclosing
    /// expression.
    /// </summary>
    public static bool ContainsAwait(IrExpression expression) =>
        expression is IrAwaitExpression
        || IrNodeChildren.ExpressionsOf(expression).Any(ContainsAwait);
}
