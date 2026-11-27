using Metano.Compiler.Diagnostics;
using Metano.Compiler.Frontend.Roslyn;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The forwarders of a less accessible inherited member that the implementing class
/// cannot publish (#822): the output keeps the 4.0.0 path, and a warning (MS0001) says
/// so instead of widening the inherited member silently.
/// </summary>
public static partial class NarrowedClaimForwarders
{
    /// <summary>
    /// Reports the dropped explicit implementation <paramref name="member"/> when it
    /// forwards to a less accessible inherited member the class cannot publish it over:
    /// the inherited member is then emitted public, or, when another explicit
    /// implementation claims the same name, it stays internal although a public
    /// interface member leans on it.
    /// </summary>
    internal static void ReportUnpublished(ISymbol member, OverloadGroupingPolicy policy)
    {
        if (
            !policy.HidesInternalMembers
            || ExplicitImplementationLowering.Resolve(member, policy.Target) is not { } lowering
            || UnpublishedMessageOf(lowering, policy) is not { } message
        )
            return;

        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                member.Locations.FirstOrDefault()
            )
        );
    }

    private static string? UnpublishedMessageOf(
        ExplicitImplementation lowering,
        OverloadGroupingPolicy policy
    )
    {
        if (NarrowedClaimOf(lowering) is { } claim)
            return ObstacleOf(claim, policy) is { } obstacle
                ? WidenedMessage(claim, obstacle)
                : null;

        return MutualClaimOf(lowering) is { } mutual ? MutualClaimMessage(mutual) : null;
    }

    /// <summary>
    /// Two explicit implementations of different interfaces that forward to the same
    /// less accessible inherited member under one name: each finds the other first as
    /// the member that claims the name, so neither is published (<c>int
    /// IComparable.CompareTo(object? o) =&gt; CompareTo(o);</c> beside <c>int
    /// IComparable&lt;T&gt;.CompareTo(T? o) =&gt; CompareTo(o);</c>).
    /// </summary>
    private static MutualClaim? MutualClaimOf(ExplicitImplementation lowering) =>
        lowering
            is {
                Fate: ExplicitImplementationFate.Superseded,
                Member: { ContainingType: var owner } member,
                ClaimingMember: { } other
            }
        && ExplicitImplementationLowering.ImplementedInterfaceMember(other) is not null
        && SymbolEqualityComparer.Default.Equals(other.ContainingType, owner)
        && ForwardingImplementations.ForwardedMemberOf(member) is { } inherited
        && IsNarrowerThanInterface(lowering.InterfaceMember, inherited)
        && IsInheritedClaimer(inherited, owner)
            ? new MutualClaim(member, other, inherited)
            : null;

    private static string WidenedMessage(NarrowedClaim claim, string obstacle) =>
        $"Explicit implementation '{claim.Member.ToDisplayString()}' cannot be published on "
        + $"'{claim.Owner.Name}': {obstacle}. The base member '{NameOf(claim.Claiming)}' it "
        + "forwards to is made public instead, so every class derived from "
        + $"'{claim.Claiming.ContainingType.Name}' exposes it.";

    private static string MutualClaimMessage(MutualClaim mutual) =>
        $"Explicit implementation '{mutual.Member.ToDisplayString()}' cannot be published on "
        + $"'{mutual.Member.ContainingType.Name}': '{mutual.Other.ToDisplayString()}' claims "
        + "the same name, so neither is emitted, and the interface member is reached through "
        + $"the base member '{NameOf(mutual.Inherited)}', which stays internal, so the "
        + "target's public surface does not show it.";

    private static string NameOf(ISymbol member) => $"{member.ContainingType.Name}.{member.Name}";

    /// <summary>
    /// An explicit implementation, the explicit implementation that claims its name, and
    /// the less accessible inherited member both forward to.
    /// </summary>
    private sealed record MutualClaim(ISymbol Member, ISymbol Other, ISymbol Inherited);
}
