using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The naming half of the overload groups: each member's suffix over its
/// visible group, or its root's (spec 013 FR-001, FR-025).
/// </summary>
internal static partial class MethodOverloadGroups
{
    /// <summary>
    /// The suffix of each member of <paramref name="group"/>, declared by
    /// <paramref name="type"/> (FR-001, FR-025): an anchored member's is its
    /// root's — the root's <c>[Name]</c> wins, the member's own is ignored
    /// (FR-005) —; a synthesized member's is the target's pinned suffix
    /// (DECIDED-34); the others are named over the visible group.
    /// </summary>
    public static IReadOnlyDictionary<GroupMember, string> SuffixesOf(
        INamedTypeSymbol type,
        MethodOverloadGroup group,
        OverloadGroupingPolicy policy
    )
    {
        var suffixes = new Dictionary<GroupMember, string>();

        foreach (var member in group.Members)
        {
            if (member.SynthesizedKind is not null)
            {
                suffixes[member] = SynthesizedSuffixOf(policy);
                continue;
            }

            if (member.Anchor is not { } anchor)
                continue;

            var rootSuffix = RootSuffixOf(anchor.Root, policy);

            suffixes[member] = rootSuffix;

            if (anchor.RootMethod is { } rootMethod && member.Declaration is { } declaration)
            {
                OverloadSuffixPin
                    .Of(declaration, policy.Target)
                    ?.ReportIfIgnoredFor(rootMethod, rootSuffix);
            }
        }

        var own = group.DeclaredMembers.Where(member => member.Anchor is null).ToList();

        if (own.Count == 0)
            return suffixes;

        var inherited = VisibleInheritedOverloads(type, group).ToList();

        // An override stands for the inherited overload it overrides, which
        // the visible group already names: naming both would dead-end the
        // pair (FR-024, FR-025, #564).
        var namingMembers = group
            .DeclaredMembers.Where(member => !OverridesOneOf(inherited, member.Symbol))
            .ToList();

        // An anchored member names with its root's pin, never its own: the
        // pin it cannot use must not move its type's other overloads (FR-005).
        var visible = inherited
            .Select(overload => new PinnedOverload(
                overload,
                OverloadSuffixPin.Of(overload, policy.Target)
            ))
            .Concat(namingMembers.Select(member => PinnedOverloadOf(group, member, policy.Target)))
            .ToList();

        var memberSuffixes = OverloadGroupNaming
            .SuffixesOf(visible, policy.Target)
            .Skip(inherited.Count);

        foreach (var (member, suffix) in namingMembers.Zip(memberSuffixes))
        {
            if (member.Anchor is null)
                suffixes[member] = suffix;
        }

        return suffixes;
    }

    private static string SynthesizedSuffixOf(OverloadGroupingPolicy policy) =>
        policy.ValueEqualityNames?.FastPathSuffix
        ?? throw new InvalidOperationException(
            "A synthesized member joined a group without names."
        );

    /// <summary>
    /// The suffix the root's own declaring type gave it. Its diagnostics are
    /// reported where that type is extracted, not here.
    /// </summary>
    private static string RootSuffixOf(AnchorRoot root, OverloadGroupingPolicy policy)
    {
        if (root is not DeclaredRoot { Method: var method })
            return SynthesizedSuffixOf(policy);

        using var _ = ExtractionDiagnostics.Suppress();

        var definition = method.OriginalDefinition;
        var type = definition.ContainingType;

        if (type.TypeKind == TypeKind.Interface)
        {
            var members = InterfaceOverloadGroup.Of(type, definition.Name, policy.Target);

            return OverloadGroupNaming.SuffixesOf(members, policy.Target)[
                members.IndexOf(definition)
            ];
        }

        var group = GroupOf(definition, policy)
            ?? throw new InvalidOperationException(
                $"'{definition.ToDisplayString()}' is not a grouped method of its type."
            );

        return SuffixesOf(type, group, policy)[group.MemberOf(definition)!];
    }

    /// <summary>
    /// The inherited overloads the type can see, which name its own overloads
    /// with it: not <c>private</c>, and not <c>internal</c> to an assembly
    /// that does not grant it access.
    /// </summary>
    private static IEnumerable<IMethodSymbol> VisibleInheritedOverloads(
        INamedTypeSymbol type,
        MethodOverloadGroup group
    ) =>
        (group.InheritedOverloads ?? [])
            .OfType<DeclaredMember>()
            .Select(overload => overload.Symbol)
            .Where(overload => IsVisibleFrom(overload, type));

    /// <summary>
    /// Whether <paramref name="method"/> overrides one of the
    /// <paramref name="inherited"/> overloads, directly or through the
    /// overrides between them. The override chain decides, not the
    /// signature: a generic method's override declares type parameters of
    /// its own.
    /// </summary>
    private static bool OverridesOneOf(IReadOnlyList<IMethodSymbol> inherited, IMethodSymbol method)
    {
        for (
            var overridden = method.OverriddenMethod;
            overridden is not null;
            overridden = overridden.OverriddenMethod
        )
        {
            if (inherited.Any(overload => IsSameDefinition(overload, overridden)))
                return true;
        }

        return false;
    }

    private static bool IsNamingMember(MethodOverloadGroup group, DeclaredMember member) =>
        group.NamingMember is { } naming && member.StandsFor(naming);

    private static bool IsSameDefinition(IMethodSymbol method, IMethodSymbol other) =>
        SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, other.OriginalDefinition);

    private static bool IsVisibleFrom(IMethodSymbol member, INamedTypeSymbol type) =>
        member.DeclaredAccessibility switch
        {
            Accessibility.Private => false,
            Accessibility.Internal or Accessibility.ProtectedAndInternal =>
                member.ContainingAssembly.GivesAccessTo(type.ContainingAssembly),
            _ => true,
        };

    /// <summary>
    /// A member as its group's naming reads it: pinned by its root's
    /// <c>[Name]</c> or its own, except the member whose <c>[Name]</c> named the
    /// group (<see cref="MethodOverloadGroup.NamingMember"/>).
    /// </summary>
    private static PinnedOverload PinnedOverloadOf(
        MethodOverloadGroup group,
        DeclaredMember member,
        TargetLanguage? target
    ) =>
        new(
            member.Symbol,
            IsNamingMember(group, member)
                ? null
                : OverloadSuffixPin.Of(member.Anchor?.RootMethod ?? member.Symbol, target)
        );
}
