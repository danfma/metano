using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript modifier of a constructor (FR-049), mapped through
/// <see cref="IrToTsVisibilityMapper"/>: <c>private</c> and
/// <c>private protected</c> to <c>private</c>, <c>protected</c> to
/// <c>protected</c>, every other accessibility to public. TypeScript orders
/// them public, protected, private from the least to the most restrictive.
/// </summary>
public static class TsConstructorModifier
{
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
            : MostPermissiveModifierOf(
                IrConstructorExtractor
                    .AllExplicitConstructors(type)
                    .Select(IrConstructorExtractor.DeclaredVisibility)
                    .DefaultIfEmpty(IrVisibility.Public)
            );

    /// <summary>The modifier of one declared constructor.</summary>
    public static TsAccessibility OfConstructor(IMethodSymbol constructor) =>
        IrToTsVisibilityMapper
            .Map(IrConstructorExtractor.DeclaredVisibility(constructor))
            .Accessibility;

    /// <summary>
    /// The modifier of a constructor group from what it declares: the most
    /// permissive of its declarations, the IR counterpart of
    /// <see cref="DeclaredModifierOf"/>.
    /// </summary>
    public static TsAccessibility MostPermissiveOf(IrConstructorDeclaration constructor) =>
        MostPermissiveModifierOf(
            new[] { constructor }
                .Concat(constructor.Overloads ?? [])
                .Select(declaration => declaration.Visibility)
        );

    /// <summary>
    /// The JSDoc of one declared constructor signature of
    /// <paramref name="type"/>: the <c>@internal</c> tag of an
    /// <c>internal</c> / <c>protected internal</c> declaration. A value type
    /// never carries it, as it never carries a modifier
    /// (<see cref="KeepsPublicConstructor"/>).
    /// </summary>
    public static TsJSDoc? JSDocOf(IrClassDeclaration type, IrConstructorDeclaration constructor) =>
        KeepsPublicConstructor(type)
            ? null
            : IrToTsVisibilityMapper.Map(constructor.Visibility).JSDoc;

    /// <summary>
    /// Whether <paramref name="type"/> emits a public, untagged constructor
    /// whatever it declares: a value type, whose implicit parameterless
    /// constructor C# makes public, so <c>new S()</c> is legal anywhere.
    /// </summary>
    public static bool KeepsPublicConstructor(IrClassDeclaration type) =>
        type.Semantics.IsValueType;

    private static TsAccessibility MostPermissiveModifierOf(
        IEnumerable<IrVisibility> declarations
    ) =>
        IrToTsVisibilityMapper
            .Map(IrToTsVisibilityMapper.MostPermissiveOf(declarations))
            .Accessibility;

    /// <summary>How restrictive a modifier is: public 0, protected 1, private 2.</summary>
    private static int RestrictionOf(TsAccessibility modifier) =>
        modifier switch
        {
            TsAccessibility.Private => 2,
            TsAccessibility.Protected => 1,
            _ => 0,
        };
}
