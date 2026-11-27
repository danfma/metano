using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The published property and event forwarders (#822). Their accessors only reach the
/// inherited member of the same name, so the declaration keeps no body: it records that
/// it re-publishes that member, and the target declares the member public on the
/// implementing class in its own terms.
/// </summary>
public static partial class NarrowedClaimForwarders
{
    /// <summary>
    /// <paramref name="declaration"/> of a published property forwarder, re-publishing
    /// the inherited property it forwards to: the inherited accessors, with the setter
    /// the interface does not publish keeping the inherited visibility, and no bodies.
    /// Any other declaration is returned unchanged.
    /// </summary>
    internal static IrPropertyDeclaration RepublishInherited(
        IrPropertyDeclaration declaration,
        IPropertySymbol property,
        IrExtractionContext context
    )
    {
        if (
            ExplicitImplementationLowering.Resolve(property, context.Target) is not { } lowering
            || !IsPublishedAsOwnMember(lowering, context.GroupingPolicy)
            || lowering.ClaimingMember is not IPropertySymbol inherited
        )
            return declaration;

        return declaration with
        {
            Accessors = RepublishedAccessorsOf(property, inherited),
            SetterVisibility = UnpublishedSetterVisibility(property, inherited),
            GetterBody = null,
            SetterBody = null,
            Semantics = (declaration.Semantics ?? new IrPropertySemantics()) with
            {
                Republishes = RepublicationOf(inherited, context.Target),
            },
        };
    }

    /// <summary>
    /// Whether <paramref name="evt"/> is a published event forwarder, which re-publishes
    /// the inherited event it forwards to.
    /// </summary>
    internal static bool RepublishesInherited(IEventSymbol evt, IrExtractionContext context) =>
        IsPublishedAsOwnMember(evt, context.GroupingPolicy);

    /// <summary>
    /// A claiming property with accessors whose setter the class cannot override: an
    /// <c>init</c> one, which the target may not emit as a setter, or a private one,
    /// which a derived class cannot redeclare. An auto-property's storage is
    /// re-published as is, whatever its setter.
    /// </summary>
    private static bool HasUnpublishableSetter(
        IPropertySymbol inherited,
        OverloadGroupingPolicy policy
    ) =>
        inherited.SetMethod
            is { IsInitOnly: true }
                or { DeclaredAccessibility: Accessibility.Private }
        && RepublicationOf(inherited, policy.Target) is IrRepublishedProperty.Accessors;

    private static IrRepublishedProperty RepublicationOf(
        IPropertySymbol inherited,
        TargetLanguage? target
    ) =>
        OverrideChains.IsAutoProperty(inherited)
        && !OverrideChains.EmitsPerClassStorage(inherited, target)
            ? IrRepublishedProperty.SharedStorage
            : IrRepublishedProperty.Accessors;

    /// <summary>
    /// The inherited accessors, but for a private setter the interface does not publish:
    /// only the base class writes through it, on a receiver it types as itself.
    /// </summary>
    private static IrPropertyAccessors RepublishedAccessorsOf(
        IPropertySymbol property,
        IPropertySymbol inherited
    ) =>
        property.SetMethod is null
        && inherited.SetMethod is { DeclaredAccessibility: Accessibility.Private }
            ? IrPropertyAccessors.GetOnly
            : IrPropertyExtractor.ResolveAccessors(inherited);

    /// <summary>
    /// The visibility of the inherited setter the interface property does not publish,
    /// or <c>null</c> when the forwarder publishes it or the inherited property has none.
    /// </summary>
    private static IrVisibility? UnpublishedSetterVisibility(
        IPropertySymbol property,
        IPropertySymbol inherited
    ) =>
        property.SetMethod is null
        && inherited.SetMethod is { DeclaredAccessibility: not Accessibility.Private } setter
            ? IrVisibilityMapper.Map(setter.DeclaredAccessibility)
            : null;
}
