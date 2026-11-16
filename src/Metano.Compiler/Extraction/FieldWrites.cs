using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Finds the statements of a body that write a field: an assignment (simple or
/// compound), an increment or decrement, or a <c>ref</c> / <c>out</c> argument.
/// Symbol-based: only writes that bind to the field itself count, so
/// <c>Map["a"] = 1</c> (an indexer write through the field) does not.
/// </summary>
internal static class FieldWrites
{
    public static bool Within(SyntaxNode body, IFieldSymbol field, SemanticModel model) =>
        body.DescendantNodes()
            .Select(WrittenExpression)
            .OfType<ExpressionSyntax>()
            .Any(written =>
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(written).Symbol, field)
            );

    private static ExpressionSyntax? WrittenExpression(SyntaxNode node) =>
        node switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left,
            PrefixUnaryExpressionSyntax prefix when IsIncrementOrDecrement(prefix.Kind()) =>
                prefix.Operand,
            PostfixUnaryExpressionSyntax postfix when IsIncrementOrDecrement(postfix.Kind()) =>
                postfix.Operand,
            ArgumentSyntax argument
                when argument.RefKindKeyword.Kind()
                    is SyntaxKind.RefKeyword
                        or SyntaxKind.OutKeyword => argument.Expression,
            _ => null,
        };

    private static bool IsIncrementOrDecrement(SyntaxKind kind) =>
        kind
            is SyntaxKind.PreIncrementExpression
                or SyntaxKind.PreDecrementExpression
                or SyntaxKind.PostIncrementExpression
                or SyntaxKind.PostDecrementExpression;
}
