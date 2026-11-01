using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Classifies a syntax position against the instance field and property
/// initializers of its type (the right-hand side of the <c>=</c> on a
/// <see cref="PropertyDeclarationSyntax"/> or on a field's
/// <see cref="VariableDeclaratorSyntax"/>). The one place both the implicit
/// capture scan and the expression extractor ask the question, each with the
/// rule it needs (<see cref="Contains"/>).
/// </summary>
internal static class MemberInitializerSyntax
{
    /// <summary>
    /// Whether <paramref name="node"/> sits inside a member initializer.
    /// <para>
    /// With <paramref name="stopAtExecutableBodies"/>, a block, arrow body or
    /// accessor between the two ends the search: that code (a block lambda's
    /// body, say) runs when it is invoked, not while the initializer runs, so
    /// it does not count. Without it the containment is purely lexical: any
    /// code written inside the initializer counts, lambda bodies included.
    /// </para>
    /// </summary>
    public static bool Contains(SyntaxNode node, bool stopAtExecutableBodies)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (stopAtExecutableBodies && IsExecutableBody(ancestor))
                return false;

            if (ancestor is EqualsValueClauseSyntax clause && IsMemberInitializerClause(clause))
                return true;
        }

        return false;
    }

    private static bool IsExecutableBody(SyntaxNode node) =>
        node is BlockSyntax or ArrowExpressionClauseSyntax or AccessorDeclarationSyntax;

    private static bool IsMemberInitializerClause(EqualsValueClauseSyntax clause) =>
        clause.Parent
            is PropertyDeclarationSyntax
                or VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax },
                };
}
