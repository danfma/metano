using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Decides whether a property only exposes a primary-constructor parameter,
/// so the backends may fold the two into a single parameter property
/// (<c>constructor(readonly name: string)</c>). Only such an alias may be
/// folded: a property computed from the parameter (<c>A { get; } = a * 2</c>,
/// <c>S =&gt; s.ToUpper()</c>) or with no initializer at all (C# gives it its
/// default value, never the argument) keeps its own declaration.
/// </summary>
internal static class PrimaryConstructorPropertyAlias
{
    /// <summary>
    /// True when <paramref name="property"/> is the positional property of a
    /// record parameter, an auto-property initialized with the bare
    /// <paramref name="parameter"/> (<c>Name { get; } = name</c>), or a
    /// get-only property whose expression body is the bare parameter
    /// (<c>Name =&gt; name</c>). Every declaration of a partial property
    /// must agree.
    /// </summary>
    public static bool IsAliasOf(
        IPropertySymbol property,
        IParameterSymbol parameter,
        Compilation compilation
    ) =>
        property.DeclaringSyntaxReferences.Length > 0
        && property.DeclaringSyntaxReferences.All(reference =>
            IsAliasDeclaration(reference.GetSyntax(), parameter, compilation)
        );

    private static bool IsAliasDeclaration(
        SyntaxNode syntax,
        IParameterSymbol parameter,
        Compilation compilation
    ) =>
        syntax switch
        {
            ParameterSyntax => true,
            PropertyDeclarationSyntax declaration => AliasedExpression(declaration) is { } aliased
                && ReadsParameter(aliased, parameter, compilation),
            _ => false,
        };

    /// <summary>
    /// The expression a property hands out unchanged: its expression body, or
    /// an auto-property's initializer.
    /// </summary>
    private static ExpressionSyntax? AliasedExpression(PropertyDeclarationSyntax declaration) =>
        declaration.ExpressionBody?.Expression
        ?? (IsAutoProperty(declaration) ? declaration.Initializer?.Value : null);

    private static bool IsAutoProperty(PropertyDeclarationSyntax declaration) =>
        declaration.AccessorList?.Accessors.All(a => a.Body is null && a.ExpressionBody is null)
        ?? false;

    private static bool ReadsParameter(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        Compilation compilation
    ) =>
        expression is IdentifierNameSyntax identifier
        && SymbolEqualityComparer.Default.Equals(
            compilation.GetSemanticModel(identifier.SyntaxTree).GetSymbolInfo(identifier).Symbol,
            parameter
        );
}
