using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Groups the methods a type declares the way spec 013 dispatches them and
/// names each group's overloads (FR-001, FR-025, FR-030):
/// <list type="bullet">
///   <item>an <c>override</c> of a dispatched root, or an implementation of
///   a member of a dispatched interface group, is <b>anchored</b>: it reuses
///   its root's suffix (DECIDED-15);</item>
///   <item>an override of a root that is not dispatched (an inherited single
///   virtual) keeps its own group, as before (DECIDED-29);</item>
///   <item>every other method is its own root, named over the type's whole
///   visible group: the inherited dispatcher's overloads it can see and the
///   methods it declares (naming.md § Root anchoring).</item>
/// </list>
/// The groups of a type are computed once per target and cached.
/// </summary>
internal static partial class MethodOverloadGroups
{
    private static readonly ConditionalWeakTable<
        INamedTypeSymbol,
        ConcurrentDictionary<OverloadGroupingPolicy, IReadOnlyList<MethodOverloadGroup>>
    > Cache = new();

    /// <summary>The method groups <paramref name="type"/> declares, in first-declaration order.</summary>
    public static IReadOnlyList<MethodOverloadGroup> Of(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    ) =>
        Cache
            .GetValue(type.OriginalDefinition, static _ => new())
            .GetOrAdd(policy, _ => Compute(type.OriginalDefinition, policy));

    /// <summary>
    /// The group of <paramref name="type"/>'s definition holding the member
    /// that stands for <paramref name="method"/>; <c>null</c> when none does.
    /// </summary>
    public static MethodOverloadGroup? GroupOf(
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    ) =>
        Of(method.ContainingType, policy)
            .FirstOrDefault(group => group.MemberOf(method) is not null);

    /// <summary>
    /// The identity of a declared method or interface member
    /// (<see cref="IrOverloadRef"/>): its declaring type, its group name and
    /// its ordinal there. A declaration folded into a synthesized member
    /// (<see cref="SynthesizedMember.Replacement"/>) is that member's.
    /// </summary>
    public static IrOverloadRef RefOf(IMethodSymbol method, OverloadGroupingPolicy policy)
    {
        var definition = method.OriginalDefinition;
        var type = definition.ContainingType;

        if (type.TypeKind == TypeKind.Interface)
            return new IrOverloadRef(
                IrTypeKeys.Of(type),
                definition.Name,
                InterfaceOverloadGroup.Of(type, definition.Name, policy.Target).IndexOf(definition)
            );

        var group = GroupOf(definition, policy)
            ?? throw new InvalidOperationException(
                $"'{definition.ToDisplayString()}' is not a grouped method of its type."
            );

        return new IrOverloadRef(
            IrTypeKeys.Of(type),
            group.Name,
            group.MemberOf(definition)!.Ordinal
        );
    }

    /// <summary>
    /// The identity of the synthesized <paramref name="kind"/> member that
    /// joins a group of <paramref name="type"/> (FR-038); <c>null</c> when none
    /// does.
    /// </summary>
    public static IrOverloadRef? SynthesizedRefOf(
        INamedTypeSymbol type,
        IrSynthesizedValueMember kind,
        OverloadGroupingPolicy policy
    ) =>
        Of(type, policy)
            .Select(group => (Group: group, Member: group.Synthesized))
            .Where(entry => entry.Member?.Kind == kind)
            .Select(entry => new IrOverloadRef(
                IrTypeKeys.Of(type.OriginalDefinition),
                entry.Group.Name,
                entry.Member!.Ordinal
            ))
            .FirstOrDefault();

    /// <summary>
    /// The synthesized member <paramref name="method"/> is folded into (an
    /// <c>override GetHashCode()</c>, a struct's <c>override Equals(object)</c>);
    /// <c>null</c> for any other method.
    /// </summary>
    public static IrSynthesizedValueMember? SynthesizedKindOf(
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    ) =>
        GroupOf(method.OriginalDefinition, policy)
            ?.MemberOf(method.OriginalDefinition)
            ?.SynthesizedKind;

    private static IReadOnlyList<MethodOverloadGroup> Compute(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    )
    {
        var declared = DeclaredMethods(type, policy).ToList();

        var groups = declared
            .GroupBy(OverloadGroupNaming.GroupNameOf, StringComparer.Ordinal)
            .SelectMany(methods => GroupsNamed(type, methods.Key, methods.ToList(), policy))
            .ToList();

        return SynthesizedGroupMembers
            .Insert(type, groups, policy)
            .OrderBy(group => FirstDeclarationIndexOf(group, declared))
            .ToList();
    }

    /// <summary>
    /// Where a group sits among the type's declarations: at its first declared
    /// method, or the declaration folded into its synthesized member; a group
    /// holding only a synthesized member comes last.
    /// </summary>
    private static int FirstDeclarationIndexOf(
        MethodOverloadGroup group,
        List<IMethodSymbol> declared
    )
    {
        var first = group.Members[0].Declaration;

        return first is null ? int.MaxValue : declared.IndexOf(first);
    }

    /// <summary>
    /// The groups of the methods a type declares under one name: the main
    /// group, and the overrides of roots that are not dispatched apart.
    /// </summary>
    private static IEnumerable<MethodOverloadGroup> GroupsNamed(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<IMethodSymbol> methods,
        OverloadGroupingPolicy policy
    )
    {
        var members = methods
            .Select(
                (method, ordinal) =>
                    new DeclaredMember(method, ordinal, AnchorOf(type, method, policy))
            )
            .ToList();

        var (main, segregated) = SplitSegregatedOverrides(members, policy.Target);

        if (main.Count > 0)
        {
            yield return new MethodOverloadGroup(
                name,
                main,
                InheritedOverloadsOf(type, name, main, policy),
                IsSegregatedOverrides: false
            );
        }

        if (segregated.Count > 0)
        {
            yield return new MethodOverloadGroup(
                name,
                segregated,
                InheritedOverloads: null,
                IsSegregatedOverrides: true
            );
        }
    }

    /// <summary>
    /// The members that form the name's main group, and the overrides kept in
    /// a group of their own (DECIDED-29). An override of a BCL virtual
    /// (<c>object.Equals</c>, <c>ToString</c>) and an overload declared beside
    /// it would both claim the plain name (<c>Equals(T)</c> and
    /// <c>Equals(object)</c>: two <c>equals</c> methods): they form one
    /// dispatched group instead, the override an overload like the others
    /// (#677). The override of an emitted virtual keeps its own group.
    /// </summary>
    private static (List<GroupMember> Main, List<GroupMember> Segregated) SplitSegregatedOverrides(
        List<DeclaredMember> members,
        TargetLanguage? target
    )
    {
        var segregated = members.Where(IsSegregatedOverride).ToList();
        var main = members.Except(segregated).ToList<GroupMember>();

        var joinsTheMainGroup =
            main.Count > 0
            && segregated.Count > 0
            && segregated.All(member => OverridesBclVirtual(member, target));

        return joinsTheMainGroup ? ([.. members], []) : (main, [.. segregated]);
    }

    private static bool IsSegregatedOverride(DeclaredMember member) =>
        member.Symbol.IsOverride && member.Anchor is null;

    /// <summary>
    /// Whether the override chain of <paramref name="member"/> ends in a virtual
    /// of a type the target does not emit (a BCL one such as
    /// <c>object.Equals</c>), so no emitted declaration owns its name.
    /// </summary>
    private static bool OverridesBclVirtual(DeclaredMember member, TargetLanguage? target)
    {
        var root = member.Symbol.OriginalDefinition;

        while (root.OverriddenMethod is { } overridden)
            root = overridden.OriginalDefinition;

        return !EmittedTypes.IsEmittedBase(root.ContainingType, target);
    }

    /// <summary>
    /// The methods the class extractor declares, in declaration order:
    /// ordinary methods, operators, conversions and the explicit
    /// implementations it keeps, minus <c>[Ignore]</c>d and inlined ones.
    /// </summary>
    private static IEnumerable<IMethodSymbol> DeclaredMethods(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    ) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => IsDeclaredGroupMethod(method, policy));

    private static bool IsDeclaredGroupMethod(
        IMethodSymbol method,
        OverloadGroupingPolicy policy
    ) =>
        !method.IsImplicitlyDeclared
        && !method.IsCompilerGenerated()
        && !SymbolHelper.HasIgnore(method, policy.Target)
        && IsUserMethod(method)
        && !ExplicitImplementationFates.IsDropped(method, policy)
        && !SymbolHelper.IsInlinedAtCallSite(method);

    private static bool IsUserMethod(IMethodSymbol method) =>
        method.MethodKind
            is MethodKind.Ordinary
                or MethodKind.UserDefinedOperator
                or MethodKind.Conversion
                or MethodKind.ExplicitInterfaceImplementation;
}
