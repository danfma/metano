using Metano.Annotations;
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

        var shape = ValueEqualityShapes.Of(type, policy.Target);

        return Kinds
            .Where(shape.Synthesizes)
            .Select(kind => (Kind: kind, Name: policy.NameOf(kind)))
            .Where(entry => entry.Name is not null)
            .Aggregate(groups, (inserted, entry) =>
                InsertKind(type, inserted, entry.Kind, entry.Name!, policy)
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

        var joined = groups.FirstOrDefault(group =>
            IsSpelled(group, name, target) && HasNonReplacingMember(type, group, kind, target)
        );

        var inherited = InsertedByBase(type, kind, policy);

        if (joined is null && inherited is null)
            return groups;

        var groupName = joined?.Name ?? inherited!.Value.GroupName;
        var replacement = FoldedReplacementOf(type, kind, target);

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

        return remaining
            .Select(group =>
                IsSameGroup(group, joined)
                    ? group with
                    {
                        Members = [.. group.Members, member],
                        NamingMember = namingMember,
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
    /// Whether the target spells <paramref name="group"/> <paramref name="name"/>
    /// (FR-039): a dispatched group by its camelCased C# name — a <c>[Name]</c>
    /// on an overload pins its suffix —, a lone method by its raw <c>[Name]</c>
    /// when it has one (never <see cref="OverloadEmittedName.NameOverrideOf"/>,
    /// which reads whether the group is dispatched). Operators and static
    /// methods never take a synthesized member.
    /// </summary>
    private static bool IsSpelled(MethodOverloadGroup group, string name, TargetLanguage? target)
    {
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
        TargetLanguage? target
    )
    {
        var replacing = ReplacingDeclarationsOf(type, kind, target);

        return group.DeclaredMembers.Any(member => !replacing.Any(member.StandsFor));
    }

    /// <summary>The declarations C# uses instead of synthesizing <paramref name="kind"/>.</summary>
    private static IReadOnlyList<IMethodSymbol> ReplacingDeclarationsOf(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        TargetLanguage? target
    ) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => ValueEqualityShapes.ReplacingEqualsOf(type, target),
            IrSynthesizedValueMember.HashCode => ValueEqualityShapes.ReplacingHashCodeOf(
                type,
                target
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
        TargetLanguage? target
    ) =>
        kind switch
        {
            IrSynthesizedValueMember.Equals => ValueEqualityShapes.FoldedEqualsOf(type, target),
            IrSynthesizedValueMember.HashCode => ValueEqualityShapes.ReplacingHashCodeOf(
                type,
                target
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
            .Select(ancestor =>
                (
                    Ancestor: ancestor,
                    Group: MethodOverloadGroups
                        .Of(ancestor, policy)
                        .FirstOrDefault(candidate => candidate.Synthesized?.Kind == kind)
                )
            )
            .Where(entry => entry.Group is not null)
            .Select(entry =>
                ((SynthesizedRoot Root, string GroupName)?)
                    (
                        new SynthesizedRoot(entry.Ancestor.OriginalDefinition, kind),
                        entry.Group!.Name
                    )
            )
            .LastOrDefault();

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
