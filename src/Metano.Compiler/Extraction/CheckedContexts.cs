using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The <c>checked</c> / <c>unchecked</c> context a syntax node sits in, for
/// the conversions Roslyn reports without an operation that carries
/// <c>IsChecked</c> (a <c>foreach</c> element conversion) and for constants
/// folded inside a context (spec 012 FR-023). Lambdas and local functions
/// inherit the context of the block that declares them, as C# specifies.
/// </summary>
internal static class CheckedContexts
{
    /// <summary>
    /// The innermost <c>checked</c> / <c>unchecked</c> expression or
    /// statement enclosing <paramref name="node"/> within its member, or
    /// <c>null</c>.
    /// </summary>
    public static SyntaxNode? Innermost(SyntaxNode node) =>
        node.AncestorsAndSelf()
            .TakeWhile(ancestor => ancestor is not MemberDeclarationSyntax)
            .FirstOrDefault(ancestor =>
                ancestor is CheckedExpressionSyntax or CheckedStatementSyntax
            );

    /// <summary>
    /// Whether <paramref name="node"/> evaluates in a checked context: the
    /// innermost context decides, and the compilation's
    /// <c>CheckOverflow</c> option applies outside every context.
    /// </summary>
    public static bool IsChecked(SyntaxNode node, Compilation compilation) =>
        Innermost(node) is { } context
            ? context.IsKind(SyntaxKind.CheckedExpression)
                || context.IsKind(SyntaxKind.CheckedStatement)
            : compilation.Options.CheckOverflow;
}
