using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A <c>dictionary.Keys</c> or <c>dictionary.Values</c> read on one of the
/// BCL dictionary types (<see cref="RoslynTypeQueries.IsBclDictionaryDefinition"/>).
/// A view holds one element per dictionary entry, which lets the extractor
/// answer some questions about the view from the dictionary itself.
/// </summary>
/// <param name="Dictionary">The expression the view is read from.</param>
/// <param name="DictionaryType">The dictionary type that declares the view.</param>
/// <param name="IsKeys"><c>true</c> for <c>Keys</c>, <c>false</c> for <c>Values</c>.</param>
internal readonly record struct DictionaryViewRead(
    ExpressionSyntax Dictionary,
    INamedTypeSymbol DictionaryType,
    bool IsKeys
)
{
    /// <summary>
    /// Matches <paramref name="expression"/> when it is a member access
    /// bound to the <c>Keys</c> or <c>Values</c> property of a BCL
    /// dictionary type; <c>null</c> otherwise.
    /// </summary>
    public static DictionaryViewRead? Match(ExpressionSyntax expression, SemanticModel semantic)
    {
        if (expression is not MemberAccessExpressionSyntax access)
            return null;

        if (
            semantic.GetSymbolInfo(access).Symbol
            is not IPropertySymbol { IsStatic: false, Name: "Keys" or "Values" } view
        )
            return null;

        if (!view.ContainingType.IsBclDictionaryDefinition())
            return null;

        return new DictionaryViewRead(access.Expression, view.ContainingType, view.Name == "Keys");
    }
}
