using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether a parameter of an explicit constructor is promoted to a property
/// (#801): only when the property is an auto-property the constructor body
/// initializes from the bare parameter (<c>Name = name;</c> or
/// <c>this.Name = name;</c>). A computed property keeps its getter, so a
/// parameter named like it (<c>NoInit(string n)</c> beside
/// <c>string N =&gt; _name + ...</c>) stays a plain parameter. Without syntax to
/// inspect (a metadata constructor), the name match decides, as the declaring
/// project already chose.
/// </summary>
internal static class ExplicitConstructorPromotion
{
    public static bool Promotes(
        IPropertySymbol property,
        IParameterSymbol parameter,
        Compilation? compilation
    )
    {
        if (
            compilation is null
            || parameter.ContainingSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not ConstructorDeclarationSyntax constructor
        )
            return true;

        if (!IsAutoProperty(property))
            return false;

        var model = compilation.GetSemanticModel(constructor.SyntaxTree);

        return TopLevelExpressionsOf(constructor)
            .Any(expression => AssignsParameterTo(expression, property, parameter, model));
    }

    private static bool IsAutoProperty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.Length > 0
        && property.DeclaringSyntaxReferences.All(reference =>
            reference.GetSyntax() switch
            {
                ParameterSyntax => true,
                PropertyDeclarationSyntax declaration =>
                    PrimaryConstructorPropertyAlias.IsAutoProperty(declaration),
                _ => false,
            }
        );

    /// <summary>The top-level expressions of the constructor body.</summary>
    private static IEnumerable<ExpressionSyntax> TopLevelExpressionsOf(
        ConstructorDeclarationSyntax constructor
    ) =>
        constructor.ExpressionBody is { } arrow
            ? [arrow.Expression]
            : (constructor.Body?.Statements ?? [])
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.Expression);

    private static bool AssignsParameterTo(
        ExpressionSyntax expression,
        IPropertySymbol property,
        IParameterSymbol parameter,
        SemanticModel model
    ) =>
        expression
            is AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                Left: var target,
                Right: IdentifierNameSyntax value,
            }
        && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(target).Symbol, property)
        && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(value).Symbol, parameter);
}
