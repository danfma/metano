using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The members an explicit interface implementation leans on: when a target drops
/// <c>void IDisposable.Dispose() =&gt; Dispose();</c> as superseded, the member that
/// claims the name (<c>internal void Dispose()</c>, possibly inherited from another
/// class) is what satisfies the public interface member at run time. Such a member is
/// part of the contract, so it is no <c>internal</c> member (#819): a target that
/// hides internal members from its public surface must not hide it.
/// <para>
/// It reads <c>Fate == Superseded</c> from <see cref="ExplicitImplementationLowering"/>
/// and not <see cref="ExplicitImplementationFates.IsDropped"/>: the
/// <c>KeepsSupersededEquatable</c> case keeps the explicit member, which only widens
/// visibility here. It scans every source type of the compilation, emitted or not, so a
/// claiming member may be promoted for a forwarder no emitted class carries.
/// The rule is target-neutral: it only widens the IR visibility, and a target that
/// ignores <c>Internal</c> (Dart) is unaffected.
/// </para>
/// </summary>
internal static class InterfaceContractClaims
{
    private const int NoTarget = -1;

    private static readonly ConditionalWeakTable<
        Compilation,
        ConcurrentDictionary<int, HashSet<ISymbol>>
    > Cache = new();

    /// <summary>
    /// Whether <paramref name="member"/> is the claiming member of a superseded
    /// explicit implementation of a public interface member somewhere in
    /// <paramref name="compilation"/>.
    /// </summary>
    public static bool IsClaimedByPublicContract(
        ISymbol member,
        Compilation compilation,
        TargetLanguage? target
    ) =>
        Cache
            .GetValue(compilation, _ => new())
            .GetOrAdd(target is { } t ? (int)t : NoTarget, _ => Collect(compilation, target))
            .Contains(member.OriginalDefinition);

    private static HashSet<ISymbol> Collect(Compilation compilation, TargetLanguage? target)
    {
        var claims = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var type in TypesOf(compilation.Assembly.GlobalNamespace))
        {
            foreach (var member in type.GetMembers())
            {
                if (ClaimOf(member, target) is { } claim)
                    claims.Add(claim.OriginalDefinition);
            }
        }

        return claims;
    }

    private static ISymbol? ClaimOf(ISymbol member, TargetLanguage? target)
    {
        if (ExplicitImplementationLowering.Resolve(member, target) is not { } lowering)
            return null;

        return IsSupersededByPublicContract(lowering) ? lowering.ClaimingMember : null;
    }

    private static bool IsSupersededByPublicContract(ExplicitImplementation lowering) =>
        lowering.Fate is ExplicitImplementationFate.Superseded
        && lowering.ClaimingMember is not null
        && IsPublic(lowering.InterfaceMember.ContainingType);

    private static bool IsPublic(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not Accessibility.Public)
                return false;
        }

        return true;
    }

    private static IEnumerable<INamedTypeSymbol> TypesOf(INamespaceSymbol container)
    {
        foreach (var type in container.GetTypeMembers())
        {
            yield return type;

            foreach (var nested in NestedTypesOf(type))
                yield return nested;
        }

        foreach (var child in container.GetNamespaceMembers())
        {
            foreach (var type in TypesOf(child))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypesOf(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;

            foreach (var deeper in NestedTypesOf(nested))
                yield return deeper;
        }
    }
}
