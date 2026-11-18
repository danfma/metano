using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// What a declared method is anchored on: the dispatched root of its override
/// chain or the dispatched interface members it implements (spec 013 FR-025).
/// </summary>
internal static partial class MethodOverloadGroups
{
    /// <summary>
    /// What <paramref name="method"/> is anchored on: the root of its
    /// override chain when that root's group is dispatched, else the members
    /// of dispatched interface groups it implements; <c>null</c> for its own
    /// root. Static members are never anchored.
    /// </summary>
    private static MemberAnchor? AnchorOf(
        INamedTypeSymbol type,
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    )
    {
        if (method.IsStatic)
            return null;

        var target = policy.Target;

        var interfaceRoots = InterfaceRootsOf(type, method, target);

        // A root of an interface the type implements more than once keeps
        // runtime dispatch under its name (DECIDED-30): every implementation
        // anchors on it, an override of a base's included.
        var constructedRoot = interfaceRoots.FirstOrDefault(root =>
            MultiplyConstructedInterfaces.AreMultiplyConstructed(type, root.ContainingType)
        );

        if (constructedRoot is not null)
            return new MemberAnchor(new DeclaredRoot(constructedRoot), KindOf(method), []);

        // An override reuses its root's names; the base it overrides already
        // declares the forwarding members of any further interface roots.
        if (method.IsOverride && DispatchedOverrideRootOf(method, policy) is { } root)
            return new MemberAnchor(new DeclaredRoot(root), IrOverloadAnchorKind.Override, []);

        if (interfaceRoots.Count == 0)
            return null;

        // An interface root is implemented, never overridden, even by an
        // override of an undispatched base virtual: the base declares no fast
        // path under the root's name (the claim pass marks the ones it does).
        // The overridden base already declares the further roots' forwarders.
        var furtherRoots = method.IsOverride
            ? new List<IMethodSymbol>()
            : interfaceRoots.Skip(1).ToList();

        return new MemberAnchor(
            new DeclaredRoot(interfaceRoots[0]),
            IrOverloadAnchorKind.Implementation,
            furtherRoots,
            furtherRoots
                .Prepend(interfaceRoots[0])
                .Where(root => BaseImplements(type, root, target))
                .ToList()
        );
    }

    /// <summary>
    /// Whether an emitted base class already implements
    /// <paramref name="root"/> (the interface mapping of the type's base), so
    /// the type re-implements it — a <c>new</c> method or an explicit
    /// re-implementation — and its root names override the base's.
    /// </summary>
    private static bool BaseImplements(
        INamedTypeSymbol type,
        IMethodSymbol root,
        TargetLanguage? target
    ) =>
        type.BaseType is { } baseType
        && baseType.FindImplementationForInterfaceMember(root) is IMethodSymbol implementation
        && EmittedTypes.IsEmittedBase(implementation.ContainingType, target);

    private static IrOverloadAnchorKind KindOf(IMethodSymbol method) =>
        method.IsOverride ? IrOverloadAnchorKind.Override : IrOverloadAnchorKind.Implementation;

    /// <summary>
    /// The first declaration of <paramref name="method"/>'s override chain that
    /// an emitted class declares, when it sits in a dispatched group (DECIDED-29
    /// keeps the others). A BCL root such as <c>object.Equals</c> is not
    /// emitted, so the chain's first emitted override stands for it.
    /// </summary>
    private static IMethodSymbol? DispatchedOverrideRootOf(
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    )
    {
        // The first emitted declaration of the chain stands for the root when
        // the chain continues into a BCL virtual (`object.Equals`) that no
        // emitted class owns.
        var firstEmitted = OverrideChainOf(method)
            .LastOrDefault(declaration =>
                EmittedTypes.IsEmittedBase(declaration.ContainingType, policy.Target)
            );

        return firstEmitted is not null && GroupOf(firstEmitted, policy) is { IsDispatched: true }
            ? firstEmitted
            : null;
    }

    /// <summary>
    /// The declarations <paramref name="method"/> overrides, nearest first, up to
    /// the virtual that starts the chain (original definitions).
    /// </summary>
    private static IEnumerable<IMethodSymbol> OverrideChainOf(IMethodSymbol method)
    {
        for (
            var overridden = method.OriginalDefinition.OverriddenMethod;
            overridden is not null;
            overridden = overridden.OriginalDefinition.OverriddenMethod
        )
            yield return overridden.OriginalDefinition;
    }

    /// <summary>
    /// The members of dispatched groups of transpiled interfaces that
    /// <paramref name="method"/> implements, in the order of the type's
    /// interfaces, constructed as the type implements them.
    /// </summary>
    private static IReadOnlyList<IMethodSymbol> InterfaceRootsOf(
        INamedTypeSymbol type,
        IMethodSymbol method,
        TargetLanguage? target
    )
    {
        var roots = new List<IMethodSymbol>();

        foreach (var implemented in type.AllInterfaces)
        {
            if (!EmittedTypes.IsTranspiledInterface(implemented, target))
                continue;

            foreach (var member in implemented.GetMembers().OfType<IMethodSymbol>())
            {
                if (!InterfaceOverloadGroup.IsMember(member, target))
                    continue;

                if (InterfaceOverloadGroup.Of(implemented, member.Name, target).Count < 2)
                    continue;

                if (Implements(type, method, member))
                    roots.Add(member);
            }
        }

        return roots;
    }

    /// <summary>
    /// Whether <paramref name="method"/> implements <paramref name="member"/>
    /// in <paramref name="type"/>: it is the interface mapping's method, or an
    /// override of it (the mapping names the base method a derived class
    /// overrides).
    /// </summary>
    private static bool Implements(
        INamedTypeSymbol type,
        IMethodSymbol method,
        IMethodSymbol member
    )
    {
        if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation)
            return false;

        for (
            IMethodSymbol? current = method;
            current is not null;
            current = current.OverriddenMethod
        )
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    implementation.OriginalDefinition
                )
            )
                return true;
        }

        return false;
    }
}
