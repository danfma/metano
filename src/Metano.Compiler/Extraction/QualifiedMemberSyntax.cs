using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A member read off an explicit receiver: <c>receiver.Name</c>, or the
/// leading <c>?.Name</c> of a null-conditional access, whose receiver is the
/// enclosing conditional access's receiver. Lets every receiver-aware
/// lowering (extension rewrites, <c>[Emit]</c>, <c>[Inline]</c>, LINQ) treat
/// both shapes alike; extracting <see cref="Receiver"/> inside a
/// null-conditional continuation yields the bound receiver, so it is never
/// re-evaluated.
/// </summary>
/// <param name="Node">The member syntax itself, for symbol lookups.</param>
/// <param name="Receiver">The syntax of the receiver the member is read from.</param>
/// <param name="Name">The member name.</param>
internal readonly record struct QualifiedMemberSyntax(
    ExpressionSyntax Node,
    ExpressionSyntax Receiver,
    SimpleNameSyntax Name
)
{
    public static QualifiedMemberSyntax Of(MemberAccessExpressionSyntax access) =>
        new(access, access.Expression, access.Name);

    /// <summary>
    /// The qualified view of <paramref name="expression"/>, or <c>null</c>
    /// when it is not a member read with an explicit receiver.
    /// </summary>
    public static QualifiedMemberSyntax? From(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax access => Of(access),
            MemberBindingExpressionSyntax binding
                when ConditionalReceiverOf(binding) is { } receiver => new(
                binding,
                receiver,
                binding.Name
            ),
            _ => null,
        };

    /// <summary>
    /// The receiver of the null-conditional access a leading
    /// <c>?.Name</c> / <c>?[i]</c> binding belongs to: the nearest enclosing
    /// <see cref="ConditionalAccessExpressionSyntax"/> whose
    /// <c>WhenNotNull</c> contains the binding.
    /// </summary>
    public static ExpressionSyntax? ConditionalReceiverOf(ExpressionSyntax binding)
    {
        for (var node = binding.Parent; node is not null; node = node.Parent)
        {
            if (
                node is ConditionalAccessExpressionSyntax access
                && access.WhenNotNull.Span.Contains(binding.Span)
            )
                return access.Expression;
        }

        return null;
    }
}
