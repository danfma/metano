using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript modifier of a constructor declared with a given IR
/// visibility (FR-049): <c>private</c> and <c>private protected</c> map to
/// <c>private</c>, <c>protected</c> to <c>protected</c>, <c>internal</c> and
/// <c>protected internal</c> to public (issue #445 — the assembly-wide band
/// shares <see cref="IrToTsVisibilityMapper"/>'s table). TypeScript orders
/// them public, protected, private from the least to the most restrictive.
/// </summary>
public static class TsConstructorModifier
{
    public static TsAccessibility Of(IrVisibility visibility) =>
        visibility is IrVisibility.PrivateProtected
            ? TsAccessibility.Private
            : IrToTsVisibilityMapper.Of(visibility);

    /// <summary>How restrictive a modifier is: public 0, protected 1, private 2.</summary>
    public static int RestrictionOf(TsAccessibility modifier) =>
        modifier switch
        {
            TsAccessibility.Private => 2,
            TsAccessibility.Protected => 1,
            _ => 0,
        };

    public static bool IsNarrowerThan(this TsAccessibility modifier, TsAccessibility other) =>
        RestrictionOf(modifier) > RestrictionOf(other);

    /// <summary>
    /// Whether <paramref name="modifier"/> restricts construction:
    /// <c>protected</c> or <c>private</c>.
    /// </summary>
    public static bool IsNonPublic(this TsAccessibility modifier) =>
        modifier is not TsAccessibility.Public;

    /// <summary>
    /// The modifier a type's constructor takes from what it declares, before
    /// the cross-file rules of <see cref="ConstructorAccessPolicy"/>: the most
    /// permissive over its declared constructors
    /// (<see cref="IrConstructorExtractor.AllExplicitConstructors"/>, each
    /// through <see cref="IrConstructorExtractor.DeclaredVisibility"/>, the
    /// function the IR is built from), public for a value type or a type
    /// declaring none. Works on source and metadata types alike, so a consumer
    /// judges a referenced type the way its package emitted it.
    /// </summary>
    public static TsAccessibility DeclaredModifierOf(INamedTypeSymbol type) =>
        type.IsValueType
            ? TsAccessibility.Public
            : IrConstructorExtractor
                .AllExplicitConstructors(type)
                .Select(OfConstructor)
                .DefaultIfEmpty(TsAccessibility.Public)
                .MinBy(RestrictionOf);

    /// <summary>The modifier of one declared constructor.</summary>
    public static TsAccessibility OfConstructor(IMethodSymbol constructor) =>
        Of(IrConstructorExtractor.DeclaredVisibility(constructor));

    /// <summary>
    /// The IR visibility the constructor group's most permissive declaration
    /// carries — the declaration whose accessibility the group emits, and what
    /// the <c>@internal</c> tagging of issue #445 reads.
    /// </summary>
    public static IrVisibility MostPermissiveVisibilityOf(IrConstructorDeclaration constructor) =>
        new[] { constructor }
            .Concat(constructor.Overloads ?? [])
            .OrderBy(declaration => RestrictionOf(Of(declaration.Visibility)))
            .First()
            .Visibility;
}
