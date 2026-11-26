using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Whether a parameter of an explicit constructor is promoted to a property
/// (#801): only when the property is an auto-property the constructor body
/// initializes from the bare parameter (<c>Name = name;</c> or
/// <c>this.Name = name;</c>) in its leading run of plain stores to the
/// instance whose values never read it, so the promoted store, which runs
/// before the body, cannot be observed (a <c>return</c>, a read of the
/// member or a store through another receiver before it ends the run). A computed property keeps its getter, so a
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

        foreach (var statement in LeadingStatementsOf(constructor))
        {
            if (AssignsParameterTo(statement, property, parameter, model))
                return true;

            if (!IsUnobservedStore(statement, property.ContainingType, model))
                return false;
        }

        return false;
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

    /// <summary>
    /// The constructor body's statements as expressions, in order, up to the
    /// first one that is not an expression statement; the body of an
    /// expression-bodied constructor is its one expression.
    /// </summary>
    private static IEnumerable<ExpressionSyntax> LeadingStatementsOf(
        ConstructorDeclarationSyntax constructor
    ) =>
        constructor.ExpressionBody is { } arrow
            ? [arrow.Expression]
            : (constructor.Body?.Statements ?? [])
                .TakeWhile(statement => statement is ExpressionStatementSyntax)
                .Select(statement => ((ExpressionStatementSyntax)statement).Expression);

    /// <summary>
    /// Whether <paramref name="expression"/> is a plain store to a field or
    /// auto-property of the instance (<c>X = value</c> / <c>this.X = value</c>)
    /// whose value never reads the instance, so a promoted store after it still
    /// runs before anything can observe the member (#801).
    /// </summary>
    private static bool IsUnobservedStore(
        ExpressionSyntax expression,
        INamedTypeSymbol type,
        SemanticModel model
    ) =>
        expression
            is AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                Left: var target,
                Right: var value,
            }
        && IsInstanceMember(target, type, model)
        && !ReadsInstance(value, type, model);

    private static bool IsInstanceMember(
        ExpressionSyntax target,
        INamedTypeSymbol type,
        SemanticModel model
    ) =>
        target
            is IdentifierNameSyntax
                or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
        && model.GetSymbolInfo(target).Symbol
            is { IsStatic: false, Kind: SymbolKind.Field or SymbolKind.Property } member
        && SymbolEqualityComparer.Default.Equals(member.ContainingType, type)
        && (member is not IPropertySymbol property || IsPlainStorage(property));

    /// <summary>
    /// A property whose setter only stores: a non-overridable auto-property. A
    /// setter with a body, or one a derived class may override, could read
    /// the instance.
    /// </summary>
    private static bool IsPlainStorage(IPropertySymbol property) =>
        IsAutoProperty(property)
        && !property.IsVirtual
        && !property.IsAbstract
        && property is not { IsOverride: true, IsSealed: false };

    private static bool ReadsInstance(
        ExpressionSyntax value,
        INamedTypeSymbol type,
        SemanticModel model
    ) =>
        value
            .DescendantNodesAndSelf()
            .Any(node =>
                node is ThisExpressionSyntax or BaseExpressionSyntax
                || (
                    node is IdentifierNameSyntax identifier
                    && !IsQualifiedName(identifier)
                    && model.GetSymbolInfo(identifier).Symbol is { IsStatic: false } symbol
                    && symbol.Kind
                        is SymbolKind.Field
                            or SymbolKind.Property
                            or SymbolKind.Method
                            or SymbolKind.Event
                    && IsMemberOfTypeOrBase(symbol, type)
                )
            );

    private static bool IsQualifiedName(IdentifierNameSyntax identifier) =>
        identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier;

    private static bool IsMemberOfTypeOrBase(ISymbol symbol, INamedTypeSymbol type)
    {
        for (var current = (INamedTypeSymbol?)type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(symbol.ContainingType, current))
                return true;
        }

        return false;
    }

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
