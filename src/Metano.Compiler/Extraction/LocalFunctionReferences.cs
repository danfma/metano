using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether a local function is used: referenced, by symbol, anywhere in its
/// enclosing body other than inside its own body (a self-recursive call) or a
/// <c>nameof</c> argument.
/// </summary>
internal static class LocalFunctionReferences
{
    public static bool IsReferenced(SemanticModel model, LocalFunctionStatementSyntax function)
    {
        var symbol = model.GetDeclaredSymbol(function);
        var scope = function.Parent;

        if (symbol is null || scope is null)
            return false;

        return scope
            .DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Where(name => name.Identifier.ValueText == symbol.Name)
            .Where(name => !function.Span.Contains(name.Span) && !IsInsideNameOf(name))
            .Any(name =>
                SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(name).Symbol?.OriginalDefinition,
                    symbol
                )
                || model
                    .GetSymbolInfo(name)
                    .CandidateSymbols.Any(candidate =>
                        SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, symbol)
                    )
            );
    }

    private static bool IsInsideNameOf(SyntaxNode name) =>
        name.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(call =>
                call.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            );
}
