using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Maps Roslyn <see cref="Accessibility"/> to <see cref="IrVisibility"/>.
/// </summary>
public static class IrVisibilityMapper
{
    public static IrVisibility Map(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => IrVisibility.Public,
            Accessibility.Protected => IrVisibility.Protected,
            Accessibility.Internal => IrVisibility.Internal,
            Accessibility.ProtectedOrInternal => IrVisibility.ProtectedInternal,
            Accessibility.ProtectedAndInternal => IrVisibility.PrivateProtected,
            Accessibility.Private => IrVisibility.Private,
            _ => IrVisibility.Public,
        };

    /// <summary>
    /// The visibility of a method, property or event, judged by the root of
    /// its override chain. C# makes an override in another assembly of a
    /// <c>protected internal</c> member declare <c>protected</c>, yet the
    /// member keeps the root's accessibility: the override stays
    /// <see cref="IrVisibility.ProtectedInternal"/>, so a backend emits it
    /// the way it emits the member it overrides.
    /// </summary>
    public static IrVisibility MapMember(ISymbol member) =>
        RootDeclarationOf(member).DeclaredAccessibility is Accessibility.ProtectedOrInternal
            ? IrVisibility.ProtectedInternal
            : Map(member.DeclaredAccessibility);

    /// <summary>
    /// <see cref="MapMember(ISymbol)"/>, except that an <c>internal</c> or
    /// <c>private protected</c> member that an explicit implementation of a public
    /// interface member forwards to is <see cref="IrVisibility.Public"/>: it satisfies
    /// the interface contract, so it is not internal API (#819).
    /// </summary>
    public static IrVisibility MapMember(ISymbol member, IrExtractionContext context)
    {
        var visibility = MapMember(member);

        return IsNarrowerThanItsContract(visibility, member, context)
            ? IrVisibility.Public
            : visibility;
    }

    private static bool IsNarrowerThanItsContract(
        IrVisibility visibility,
        ISymbol member,
        IrExtractionContext context
    ) =>
        visibility is IrVisibility.Internal or IrVisibility.PrivateProtected
        && context.Compilation is { } compilation
        && InterfaceContractClaims.IsClaimedByPublicContract(
            member,
            compilation,
            context.GroupingPolicy
        );

    private static ISymbol RootDeclarationOf(ISymbol member)
    {
        var current = member;

        while (OverriddenMemberOf(current) is { } overridden)
            current = overridden;

        return current;
    }

    private static ISymbol? OverriddenMemberOf(ISymbol member) =>
        member switch
        {
            IMethodSymbol method => method.OverriddenMethod,
            IPropertySymbol property => property.OverriddenProperty,
            IEventSymbol @event => @event.OverriddenEvent,
            _ => null,
        };
}
