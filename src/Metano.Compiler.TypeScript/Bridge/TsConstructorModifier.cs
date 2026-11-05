using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript modifier and overload-signature JSDoc of a constructor
/// (FR-049), each declaration mapped through <see cref="IrToTsVisibilityMapper"/>.
/// </summary>
public static class TsConstructorModifier
{
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
            : IrToTsVisibilityMapper.MostPermissiveOf(
                IrConstructorExtractor
                    .AllExplicitConstructors(type)
                    .Select(OfConstructor)
                    .DefaultIfEmpty(TsAccessibility.Public)
            );

    /// <summary>The modifier of one declared constructor.</summary>
    public static TsAccessibility OfConstructor(IMethodSymbol constructor) =>
        MapDeclared(IrConstructorExtractor.DeclaredVisibility(constructor)).Accessibility;

    /// <summary>
    /// The modifier of a constructor group from what it declares: the most
    /// permissive of its declarations, the IR counterpart of
    /// <see cref="DeclaredModifierOf"/>.
    /// </summary>
    public static TsAccessibility MostPermissiveOf(IrConstructorDeclaration constructor) =>
        IrToTsVisibilityMapper.MostPermissiveOf(
            DeclarationsOf(constructor)
                .Select(declaration => MapDeclared(declaration.Visibility).Accessibility)
        );

    /// <summary>
    /// The JSDoc of each overload signature of <paramref name="type"/>'s
    /// constructor group, emitted <paramref name="groupAccessibility"/>
    /// (<see cref="IrToTsVisibilityMapper.SignatureJSDocOf"/>). A value type
    /// never carries one, as it never carries a modifier
    /// (<see cref="KeepsPublicConstructor"/>). When every signature would be
    /// tagged, none is: <c>stripInternal</c> would drop them all and leave the
    /// <c>.d.ts</c> class an implicit public <c>new X()</c> that C# never
    /// declared, so the group is widened to its emitted accessibility instead.
    /// A single constructor is the one signature of its group, so it is never
    /// tagged.
    /// </summary>
    public static Func<IrConstructorDeclaration, TsJSDoc?> SignatureJSDocSelectorFor(
        IrClassDeclaration type,
        TsAccessibility groupAccessibility
    )
    {
        if (type.Constructor is not { } constructor || KeepsPublicConstructor(type))
            return _ => null;

        TsJSDoc? JSDocOf(IrConstructorDeclaration declaration) =>
            IrToTsVisibilityMapper.SignatureJSDocOf(
                MapDeclared(declaration.Visibility),
                groupAccessibility
            );

        var wouldStripEverySignature = DeclarationsOf(constructor)
            .All(declaration => JSDocOf(declaration) is not null);

        return wouldStripEverySignature ? _ => null : JSDocOf;
    }

    /// <summary>
    /// Whether <paramref name="type"/> emits a public, untagged constructor
    /// whatever it declares: a value type, whose implicit parameterless
    /// constructor C# makes public, so <c>new S()</c> is legal anywhere.
    /// </summary>
    public static bool KeepsPublicConstructor(IrClassDeclaration type) =>
        type.Semantics.IsValueType;

    private static TsMemberVisibility MapDeclared(IrVisibility visibility) =>
        IrToTsVisibilityMapper.MapConstructor(visibility);

    private static IEnumerable<IrConstructorDeclaration> DeclarationsOf(
        IrConstructorDeclaration constructor
    ) => new[] { constructor }.Concat(constructor.Overloads ?? []);
}
