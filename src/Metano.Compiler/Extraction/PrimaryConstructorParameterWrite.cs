using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether a reference to a primary-constructor parameter writes it. A
/// written parameter needs mutable storage, and it can diverge from a
/// property it initialized. The check is syntactic: an assignment target
/// (simple or compound), an increment or decrement, a <c>ref</c> / <c>out</c>
/// argument, a <c>ref</c> alias (<c>ref var r = ref p</c>), or an element of
/// a deconstruction target (<c>(p, q) = …</c>).
/// </summary>
internal static class PrimaryConstructorParameterWrite
{
    public static bool Writes(IdentifierNameSyntax identifier) =>
        identifier.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == identifier,
            PrefixUnaryExpressionSyntax prefix => IsIncrementOrDecrement(prefix),
            PostfixUnaryExpressionSyntax postfix => IsIncrementOrDecrement(postfix),
            RefExpressionSyntax => true,
            ArgumentSyntax argument => IsWritingArgument(argument),
            _ => false,
        };

    private static bool IsIncrementOrDecrement(ExpressionSyntax unary) =>
        unary.Kind()
            is SyntaxKind.PreIncrementExpression
                or SyntaxKind.PreDecrementExpression
                or SyntaxKind.PostIncrementExpression
                or SyntaxKind.PostDecrementExpression;

    private static bool IsWritingArgument(ArgumentSyntax argument) =>
        argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword
        || IsDeconstructionTargetElement(argument);

    /// <summary>
    /// An element of a tuple, possibly nested, that is the left side of an
    /// assignment or the variable of a <c>foreach</c>.
    /// </summary>
    private static bool IsDeconstructionTargetElement(ArgumentSyntax argument)
    {
        SyntaxNode outermost = argument;

        while (outermost.Parent is TupleExpressionSyntax or ArgumentSyntax)
            outermost = outermost.Parent;

        return outermost.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == outermost,
            ForEachVariableStatementSyntax loop => loop.Variable == outermost,
            _ => false,
        };
    }
}
