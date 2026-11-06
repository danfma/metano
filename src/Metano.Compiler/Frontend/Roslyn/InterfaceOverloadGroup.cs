using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// The overload group an interface declares under one name (spec 013
/// FR-031): its public, non-static ordinary methods of that name that the
/// target does not ignore, in declaration order. A group of two or more is
/// dispatched, so implementers anchor on its members. The single source of
/// truth for interface overload-group membership.
/// </summary>
public static class InterfaceOverloadGroup
{
    /// <summary>The members of <paramref name="type"/>'s group named <paramref name="name"/>.</summary>
    public static List<IMethodSymbol> Of(
        INamedTypeSymbol type,
        string name,
        TargetLanguage? target
    ) =>
        type
            .OriginalDefinition.GetMembers(name)
            .OfType<IMethodSymbol>()
            .Where(method => IsMember(method, target))
            .ToList();

    /// <summary>Whether <paramref name="method"/> takes part in its interface's overload group.</summary>
    public static bool IsMember(IMethodSymbol method, TargetLanguage? target) =>
        method is { MethodKind: MethodKind.Ordinary, IsStatic: false, IsImplicitlyDeclared: false }
        && method.DeclaredAccessibility == Accessibility.Public
        && !SymbolHelper.HasIgnore(method, target);

    /// <summary>Whether the group <paramref name="method"/> belongs to is dispatched (two or more).</summary>
    public static bool IsDispatched(IMethodSymbol method, TargetLanguage? target) =>
        Of(method.ContainingType, method.Name, target).Count >= 2;
}
