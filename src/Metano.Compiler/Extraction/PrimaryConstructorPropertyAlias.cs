using Metano.Annotations;
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
    /// The property that only exposes a primary-constructor
    /// <paramref name="parameter"/>, if any: a same-named property (ignoring
    /// case, so <c>name</c> pairs with <c>Name</c>; an exact-case match wins
    /// when two properties differ only by case) of the parameter's type that
    /// is a promotion candidate (<see cref="IsPromotionCandidate"/>) and only
    /// exposes the parameter (<see cref="IsAliasOf"/>). Whether the parameter
    /// is then promoted to it is <see cref="PrimaryConstructorPromotion"/>'s
    /// decision.
    /// </summary>
    public static IPropertySymbol? AliasPropertyOf(
        IParameterSymbol parameter,
        Compilation compilation,
        TargetLanguage? target
    ) =>
        CandidatesNamed(parameter.ContainingType, parameter.Name, target)
            .FirstOrDefault(property => IsAliasOf(property, parameter, compilation));

    /// <summary>
    /// <paramref name="property"/> is a record's positional property: C#
    /// synthesizes it from the parameter list and assigns it the argument
    /// before any member initializer runs.
    /// </summary>
    public static bool IsPositionalRecordProperty(IPropertySymbol property) =>
        property.ContainingType.IsRecord
        && property.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is ParameterSyntax
        );

    /// <summary>
    /// The promotion candidates of <paramref name="type"/> named
    /// <paramref name="name"/>, ignoring case, the exact-case match first.
    /// </summary>
    public static IEnumerable<IPropertySymbol> CandidatesNamed(
        INamedTypeSymbol type,
        string name,
        TargetLanguage? target
    ) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && IsPromotionCandidate(property, target)
            )
            .OrderBy(property => property.Name == name ? 0 : 1);

    /// <summary>
    /// Whether <paramref name="property"/> is emitted as a member a
    /// constructor parameter can be promoted to: declared in source, visible
    /// outside the assembly's internals, not <c>[Ignore]</c>d for
    /// <paramref name="target"/>, and not an override (whose base declares
    /// the storage) unless it is a record's positional property: C#
    /// synthesizes that override only for an abstract base property, which
    /// has no storage, so the parameter must initialize it. The constructor
    /// extraction and the capture scan share it, so a member-body read is
    /// never redirected to a property that is not emitted.
    /// </summary>
    public static bool IsPromotionCandidate(IPropertySymbol property, TargetLanguage? target) =>
        !property.IsImplicitlyDeclared
        && property.DeclaredAccessibility
            is not (Accessibility.Internal or Accessibility.NotApplicable)
        && !SymbolHelper.HasIgnore(property, target)
        && (!property.IsOverride || IsPositionalRecordProperty(property));

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
