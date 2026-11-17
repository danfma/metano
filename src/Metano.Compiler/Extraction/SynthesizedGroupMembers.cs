using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Inserts a type's synthesized value-equality members into its overload groups
/// (spec 013 FR-040, contracts/value-equality-members.md § 3). For each member the
/// type synthesizes and the target names (<see cref="OverloadGroupingPolicy"/>):
/// <list type="number">
///   <item>a user instance group the target spells like it takes it as one more
///   overload;</item>
///   <item>otherwise, when the nearest emitted base that has it inserted it, it is
///   inserted in a group of its own, anchored on the base's (FR-041);</item>
///   <item>otherwise nothing changes (NFR-001).</item>
/// </list>
/// A declaration that replaces the member does not remove it: a same-signature
/// replacement (a struct's <c>override Equals(object)</c>, an
/// <c>override GetHashCode()</c>) is folded into it; a record's typed
/// <c>Equals(R)</c> stays a declared overload the member routes to.
/// </summary>
internal static class SynthesizedGroupMembers
{
    private static readonly IrSynthesizedValueMember[] Kinds =
    [
        IrSynthesizedValueMember.Equals,
        IrSynthesizedValueMember.HashCode,
        IrSynthesizedValueMember.Copy,
    ];

    /// <summary><paramref name="groups"/> with the synthesized members inserted.</summary>
    public static IReadOnlyList<MethodOverloadGroup> Insert(
        INamedTypeSymbol type,
        IReadOnlyList<MethodOverloadGroup> groups,
        OverloadGroupingPolicy policy
    )
    {
        if (policy.ValueEqualityNames is null)
            return groups;

        var shape = ValueEqualityShapes.Of(type, policy);

        return Kinds
            .Where(shape.Synthesizes)
            .Aggregate(groups, (inserted, kind) =>
                policy.NameOf(kind) is { } name
                    ? InsertKind(type, inserted, kind, name, policy)
                    : inserted
            );
    }

    private static IReadOnlyList<MethodOverloadGroup> InsertKind(
        INamedTypeSymbol type,
        IReadOnlyList<MethodOverloadGroup> groups,
        IrSynthesizedValueMember kind,
        string name,
        OverloadGroupingPolicy policy
    )
    {
        var target = policy.Target;

        var spelled = groups.Where(group => IsSpelled(group, name, target)).ToList();

        var joined = spelled.FirstOrDefault(group =>
            HasNonReplacingMember(type, group, kind, policy)
        );

        var inherited = InsertedByBase(type, kind, policy);

        if (joined is null && inherited is null)
            return groups;

        var groupName = joined?.Name ?? inherited!.Value.GroupName;
        var replacement = FoldedReplacementOf(type, kind, policy);

        var member = new SynthesizedMember(
            kind,
            NextOrdinal(groups, groupName),
            inherited is { Root: var root }
                ? new MemberAnchor(root, IrOverloadAnchorKind.Override, [])
                : null,
            replacement
        );

        var remaining = replacement is null ? groups : WithoutMethod(groups, replacement);

        if (joined is null)
        {
            var own = new MethodOverloadGroup(
                groupName,
                [member],
                MethodOverloadGroups.InheritedOverloadsOf(type, groupName, [member], policy),
                IsSegregatedOverrides: false
            );

            return [.. remaining, own];
        }

        var namingMember = NamingMemberOf(joined, target);
        var detachedReplacement = DetachedReplacementOf(type, spelled, kind, policy);

        return remaining
            .Select(group =>
                IsSameGroup(group, joined)
                    ? group with
                    {
                        Members = [.. group.Members, member],
                        NamingMember = namingMember,
                        // Every other group spelled like it — a replacing
                        // declaration too, which would keep its plain name beside
                        // the joined dispatcher — is an error.
                        SpelledAlike =
                        [
                            .. spelled
                                .Where(other => !IsSameGroup(other, joined))
                                .Select(FirstMethodOf),
                        ],
                        DetachedReplacement = detachedReplacement,
                    }
                    : group
            )
            .ToList();
    }

    /// <summary>
    /// The lone method whose raw <c>[Name]</c> spells the group the synthesized
    /// member joined: it named the group, so it pins no suffix (FR-039).
    /// <c>null</c> when the group's C# name spells it.
    /// </summary>
    private static IMethodSymbol? NamingMemberOf(MethodOverloadGroup group, TargetLanguage? target)
    {
        if (group.IsDispatched)
            return null;

        var member = group.DeclaredMembers.First();

        return NameOverrideOf(member, target) is null ? null : member.Symbol;
    }

    /// <summary>
    /// Reports <c>MS0001</c> (an error) at each method of
    /// <see cref="MethodOverloadGroup.SpelledAlike"/>: only <paramref name="group"/>
    /// takes the synthesized member, so the others would declare a second member
    /// of that name. Called once per extracted group, outside the groups' cache.
    /// </summary>
    public static void ReportSpelledAlike(
        INamedTypeSymbol type,
        MethodOverloadGroup group,
        OverloadGroupingPolicy policy
    )
    {
        if (
            group.Synthesized is not { } synthesized
            || policy.NameOf(synthesized.Kind) is not { } name
        )
            return;

        if (group.DetachedReplacement is { } detached)
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{type.Name}.{detached.Name}' replaces the synthesized equality of "
                        + $"'{type.Name}', but its [Name] keeps it out of the overloads of "
                        + $"'{group.Name}', which take the synthesized member under '{name}'; "
                        + "the synthesized member could not reach it. Remove the [Name] or "
                        + $"rename '{group.Name}' (spec 013 FR-039).",
                    detached.Locations.FirstOrDefault()
                )
            );
        }

        foreach (var method in group.SpelledAlike)
        {
            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Error,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{type.Name}.{method.Name}' is emitted as '{name}', which the overloads "
                        + $"of '{group.Name}' already declare with the synthesized member "
                        + "of that name; give one of them another [Name] (spec 013 FR-039).",
                    method.Locations.FirstOrDefault()
                )
            );
        }
    }

    /// <summary>
    /// The typed replacing <c>Equals</c> the synthesized member routes to when it
    /// sits outside the joined group — renamed by its own <c>[Name]</c> beside a
    /// method whose <c>[Name]</c> spells <c>equals</c> —, where the route cannot
    /// reach a fast path and would loop through the dispatcher; <c>null</c>
    /// otherwise, and when its group is spelled alike, which reports it already.
    /// </summary>
    private static IMethodSymbol? DetachedReplacementOf(
        INamedTypeSymbol type,
        IReadOnlyList<MethodOverloadGroup> spelled,
        IrSynthesizedValueMember kind,
        OverloadGroupingPolicy policy
    ) =>
        kind is IrSynthesizedValueMember.Equals
        && ValueEqualityShapes.RoutedEqualsOf(type, policy) is { } routed
        && !spelled.Any(group => group.MemberOf(routed) is not null)
            ? routed
            : null;

    private static IMethodSymbol FirstMethodOf(MethodOverloadGroup group) =>
        group.DeclaredMembers.First().Symbol;

    /// <summary>
    /// Whether the target spells <paramref name="group"/> <paramref name="name"/>
    /// (FR-039): a dispatched group by its camelCased C# name — a <c>[Name]</c>
    /// on an overload pins its suffix —, a lone method by its raw <c>[Name]</c>
    /// when it has one (never <see cref="OverloadEmittedName.NameOverrideOf"/>,
    /// which reads whether the group is dispatched). Operators and static
    /// methods never take a synthesized member.
    /// </summary>
    private static bool IsSpelled(MethodOverloadGroup group, string name, TargetLanguage? target)
    {
        // A group takes one synthesized member, under the spelling that matched
        // it: once joined, its C# name no longer re-spells it for another kind.
        if (group.Synthesized is not null)
            return false;

        if (group.IsSegregatedOverrides || group.Members.Any(member => member.IsStatic))
            return false;

        if (group.DeclaredMembers.Any(member => IsOperator(member.Symbol)))
            return false;

        return SpellingOf(group, target) == name;
    }

    private static string SpellingOf(MethodOverloadGroup group, TargetLanguage? target)
    {
        var camelCased = SymbolHelper.ToCamelCase(group.Name);

        if (group.IsDispatched)
            return camelCased;

        return NameOverrideOf(group.DeclaredMembers.First(), target) ?? camelCased;
    }

    private static string? NameOverrideOf(DeclaredMember member, TargetLanguage? target) =>
        (
            ExplicitImplementationLowering.ImplementedInterfaceMember(member.Symbol)
            ?? member.Symbol
        ).GetNameOverride(target);

    private static bool IsOperator(IMethodSymbol method) =>
        method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion;

    /// <summary>
    /// Whether the group holds a method besides the declarations that replace
    /// <paramref name="kind"/>: those are C#'s implementation of the member itself
    /// and are emitted under its name as they are today.
    /// </summary>
    private static bool HasNonReplacingMember(
        INamedTypeSymbol type,
        MethodOverloadGroup group,
        IrSynthesizedValueMember kind,
        OverloadGroupingPolicy policy
    )
    {
        var replacing = ReplacingDeclarationsOf(type, kind, policy);

        return group.DeclaredMembers.Any(member => !replacing.Any(member.StandsFor));
    }

    /// <summary>The declarations C# uses instead of synthesizing <paramref name="kind"/>.</summary>
    private static IReadOnlyList<IMethodSymbol> ReplacingDeclarationsOf(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        OverloadGroupingPolicy policy
    ) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => ValueEqualityShapes.ReplacingEqualsOf(type, policy),
            IrSynthesizedValueMember.HashCode => ValueEqualityShapes.ReplacingHashCodeOf(
                type,
                policy.Target
            )
                is { } hashCode
                ? [hashCode]
                : [],
            _ => [],
        };

    /// <summary>
    /// The replacing declaration the inserted member takes the body of: one with
    /// the member's own signature (a struct's <c>override Equals(object)</c>, an
    /// <c>override GetHashCode()</c>). A typed <c>Equals</c> (a record's
    /// <c>Equals(R)</c>, a struct's interface-implementing <c>Equals(S)</c>) is
    /// routed to instead, and stays a declared overload.
    /// </summary>
    private static IMethodSymbol? FoldedReplacementOf(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        OverloadGroupingPolicy policy
    ) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => ValueEqualityShapes.FoldedEqualsOf(type, policy),
            IrSynthesizedValueMember.HashCode => ValueEqualityShapes.ReplacingHashCodeOf(
                type,
                policy.Target
            ),
            _ => null,
        };

    /// <summary>
    /// The root and group name of <paramref name="kind"/> in the topmost emitted
    /// base whose groups hold it, the first declaration of its override chain
    /// (FR-041); <c>null</c> when no emitted base inserted it.
    /// </summary>
    private static (SynthesizedRoot Root, string GroupName)? InsertedByBase(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        OverloadGroupingPolicy policy
    ) =>
        EmittedBasesOf(type, policy)
            .Select(ancestor => FoundIn(ancestor, kind, policy))
            .LastOrDefault(found => found is not null);

    /// <summary>
    /// The root and group name of <paramref name="kind"/> in
    /// <paramref name="ancestor"/>'s groups; <c>null</c> when it inserted none.
    /// </summary>
    private static (SynthesizedRoot Root, string GroupName)? FoundIn(
        INamedTypeSymbol ancestor,
        IrSynthesizedValueMember kind,
        OverloadGroupingPolicy policy
    ) =>
        MethodOverloadGroups
            .Of(ancestor, policy)
            .FirstOrDefault(candidate => candidate.Synthesized?.Kind == kind)
            is { } group
            ? (new SynthesizedRoot(ancestor.OriginalDefinition, kind), group.Name)
            : null;

    /// <summary>The emitted base classes of <paramref name="type"/>, nearest first.</summary>
    private static IEnumerable<INamedTypeSymbol> EmittedBasesOf(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    )
    {
        for (
            var ancestor = type.BaseType;
            MethodOverloadGroups.IsEmittedBase(ancestor, policy.Target);
            ancestor = ancestor!.BaseType
        )
            yield return ancestor!;
    }

    /// <summary>The ordinal after every member the type groups under <paramref name="groupName"/>.</summary>
    private static int NextOrdinal(IReadOnlyList<MethodOverloadGroup> groups, string groupName) =>
        groups
            .Where(group => group.Name == groupName)
            .SelectMany(group => group.Members)
            .Select(member => member.Ordinal + 1)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>The groups without the member that stands for <paramref name="method"/>; a group left empty is dropped.</summary>
    private static IReadOnlyList<MethodOverloadGroup> WithoutMethod(
        IReadOnlyList<MethodOverloadGroup> groups,
        IMethodSymbol method
    ) =>
        groups
            .Select(group =>
                group with
                {
                    Members = group.Members.Where(member => !member.StandsFor(method)).ToList(),
                }
            )
            .Where(group => group.Members.Count > 0)
            .ToList();

    private static bool IsSameGroup(MethodOverloadGroup group, MethodOverloadGroup other) =>
        group.Name == other.Name && group.IsSegregatedOverrides == other.IsSegregatedOverrides;
}
