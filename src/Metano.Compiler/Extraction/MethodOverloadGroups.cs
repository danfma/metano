using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// One member of an overload group (spec 013 FR-038): a method the type
/// declares (<see cref="DeclaredMember"/>) or a value-equality member C#
/// synthesizes, described by data (<see cref="SynthesizedMember"/>). The facts
/// naming and dispatch read live here, so only the class extractor tells the
/// two apart.
/// </summary>
/// <param name="Ordinal">Its position among the members the type groups under
/// its C# name (<see cref="IrOverloadRef.Ordinal"/>); a synthesized member
/// comes after every declared one.</param>
/// <param name="Anchor">The root it reuses the names of; <c>null</c> when it
/// is its own root.</param>
internal abstract record GroupMember(int Ordinal, MemberAnchor? Anchor)
{
    /// <summary>Whether the member is static.</summary>
    public abstract bool IsStatic { get; }

    /// <summary>The fewest arguments a call passes it.</summary>
    public abstract int RequiredArity { get; }

    /// <summary>The most arguments a call passes it; <see cref="int.MaxValue"/> with <c>params</c>.</summary>
    public abstract int MaximumArity { get; }

    /// <summary>Whether the member stands for <paramref name="method"/>.</summary>
    public abstract bool StandsFor(IMethodSymbol method);
}

/// <summary>A method the type declares.</summary>
/// <param name="Symbol">The declared method.</param>
internal sealed record DeclaredMember(IMethodSymbol Symbol, int Ordinal, MemberAnchor? Anchor)
    : GroupMember(Ordinal, Anchor)
{
    public override bool IsStatic => Symbol.IsStatic;

    public override int RequiredArity =>
        Symbol.Parameters.Count(parameter => !parameter.IsOptional && !parameter.IsParams);

    public override int MaximumArity =>
        Symbol.Parameters.Any(parameter => parameter.IsParams)
            ? int.MaxValue
            : Symbol.Parameters.Length;

    public override bool StandsFor(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(Symbol.OriginalDefinition, method.OriginalDefinition);
}

/// <summary>
/// A value-equality member C# synthesizes or inherits (spec 013 FR-038): its
/// shape is fixed by its kind (<c>Equals(object)</c>, <c>GetHashCode()</c>, the
/// copy behind <c>with</c> taking optional overrides) and its suffix is pinned
/// to the target's (DECIDED-34).
/// </summary>
/// <param name="Kind">Which member.</param>
/// <param name="Replacement">The user declaration that supplies its body: a
/// struct's <c>override Equals(object)</c> or an <c>override GetHashCode()</c>,
/// emitted only as this member. <c>null</c> when C# synthesizes it.</param>
internal sealed record SynthesizedMember(
    IrSynthesizedValueMember Kind,
    int Ordinal,
    MemberAnchor? Anchor,
    IMethodSymbol? Replacement = null
) : GroupMember(Ordinal, Anchor)
{
    public override bool IsStatic => false;

    public override int RequiredArity => Kind is IrSynthesizedValueMember.Equals ? 1 : 0;

    public override int MaximumArity => Kind is IrSynthesizedValueMember.HashCode ? 0 : 1;

    public override bool StandsFor(IMethodSymbol method) =>
        Replacement is not null
        && SymbolEqualityComparer.Default.Equals(
            Replacement.OriginalDefinition,
            method.OriginalDefinition
        );
}

/// <summary>
/// The root an anchored member reuses the names of (spec 013 FR-025, FR-041):
/// a declared method, or a synthesized member of an emitted base class.
/// </summary>
internal abstract record AnchorRoot;

/// <summary>A declared root: the first declaration of an override chain, or an interface member.</summary>
internal sealed record DeclaredRoot(IMethodSymbol Method) : AnchorRoot;

/// <summary>The synthesized <paramref name="Kind"/> member a base class inserted into a group.</summary>
internal sealed record SynthesizedRoot(INamedTypeSymbol Owner, IrSynthesizedValueMember Kind)
    : AnchorRoot;

/// <summary>
/// How a group member is anchored on a root it does not declare (spec 013
/// FR-025): one shape for declared and synthesized members.
/// </summary>
/// <param name="Root">The root whose names it reuses.</param>
/// <param name="Kind">Override or implementation.</param>
/// <param name="FurtherRoots">Other dispatched interface members the method
/// implements, in the order of the type's interfaces.</param>
/// <param name="ReimplementedRoots">The roots an emitted base class already
/// implements, which the member re-implements.</param>
internal sealed record MemberAnchor(
    AnchorRoot Root,
    IrOverloadAnchorKind Kind,
    IReadOnlyList<IMethodSymbol> FurtherRoots,
    IReadOnlyList<IMethodSymbol>? ReimplementedRoots = null
)
{
    /// <summary>The declared root method; <c>null</c> for a synthesized root.</summary>
    public IMethodSymbol? RootMethod => (Root as DeclaredRoot)?.Method;
}

/// <summary>
/// The members a type groups under one C# name, the way spec 013 groups
/// them for dispatch (DECIDED-15, DECIDED-29, DECIDED-33).
/// </summary>
/// <param name="Name">The C# name; an explicit implementation's interface
/// member name.</param>
/// <param name="Members">The declared methods, in declaration order, then the
/// synthesized member that joins them.</param>
/// <param name="InheritedOverloads">The overloads of the dispatcher an
/// emitted base class already declares for the name, as this type sees them
/// (type arguments substituted, base-most first); <c>null</c> when no base
/// dispatches it.</param>
/// <param name="IsSegregatedOverrides">The overrides of roots that are not
/// dispatched (an inherited single virtual): they keep their own group, as
/// before spec 013 (DECIDED-29, #159).</param>
internal sealed record MethodOverloadGroup(
    string Name,
    IReadOnlyList<GroupMember> Members,
    IReadOnlyList<GroupMember>? InheritedOverloads,
    bool IsSegregatedOverrides
)
{
    /// <summary>
    /// The lone method whose raw <c>[Name]</c> spelled the group a synthesized
    /// member joined (FR-039): its <c>[Name]</c> named the group, so it pins no
    /// suffix. <c>null</c> for every other group.
    /// </summary>
    public IMethodSymbol? NamingMember { get; init; }

    /// <summary>
    /// The first methods of other groups the target spells like the synthesized
    /// member this group took (FR-039): each would declare a second member of
    /// that name, which the class extractor reports as an error.
    /// </summary>
    public IReadOnlyList<IMethodSymbol> SpelledAlike { get; init; } = [];

    /// <summary>
    /// Whether the group's members are fast paths of a dispatched group: two
    /// or more, an inherited dispatcher, or one member anchored on a
    /// dispatched root.
    /// </summary>
    public bool IsDispatched =>
        InheritedOverloads is not null
        || Members.Count >= 2
        || Members.Any(member => member.Anchor is not null);

    /// <summary>The declared methods of the group.</summary>
    public IEnumerable<DeclaredMember> DeclaredMembers => Members.OfType<DeclaredMember>();

    /// <summary>The synthesized member that joins the group; <c>null</c> when none does.</summary>
    public SynthesizedMember? Synthesized => Members.OfType<SynthesizedMember>().FirstOrDefault();

    /// <summary>The member that stands for <paramref name="method"/>; <c>null</c> when none does.</summary>
    public GroupMember? MemberOf(IMethodSymbol method) =>
        Members.FirstOrDefault(member => member.StandsFor(method));
}

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
internal static class MethodOverloadGroups
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
            if (member is SynthesizedMember)
            {
                suffixes[member] = SynthesizedSuffixOf(policy);
                continue;
            }

            if (member.Anchor is not { } anchor)
                continue;

            var rootSuffix = RootSuffixOf(anchor.Root, policy);

            suffixes[member] = rootSuffix;

            if (
                anchor.RootMethod is { } rootMethod
                && member is DeclaredMember { Symbol: var symbol }
            )
            {
                OverloadSuffixPin
                    .Of(symbol, policy.Target)
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

        var memberSuffixes = OverloadGroupNaming.SuffixesOf(visible).Skip(inherited.Count);

        foreach (var (member, suffix) in namingMembers.Zip(memberSuffixes))
        {
            if (member.Anchor is null)
                suffixes[member] = suffix;
        }

        return suffixes;
    }

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
        GroupOf(method.OriginalDefinition, policy)?.MemberOf(method.OriginalDefinition)
            is SynthesizedMember synthesized
            ? synthesized.Kind
            : null;

    /// <summary>
    /// The name a method groups under: its C# name, or an explicit
    /// implementation's interface member name.
    /// </summary>
    public static string GroupNameOf(IMethodSymbol method) =>
        ExplicitImplementationLowering.ImplementedInterfaceMember(method)?.Name ?? method.Name;

    private static string SynthesizedSuffixOf(OverloadGroupingPolicy policy) =>
        policy.ValueEqualityNames?.FastPathSuffix
        ?? throw new InvalidOperationException(
            "A synthesized member joined a group without names."
        );

    private static IReadOnlyList<MethodOverloadGroup> Compute(
        INamedTypeSymbol type,
        OverloadGroupingPolicy policy
    )
    {
        var declared = DeclaredMethods(type, policy).ToList();

        var groups = declared
            .GroupBy(GroupNameOf, StringComparer.Ordinal)
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
        var first = group.Members[0] switch
        {
            DeclaredMember member => member.Symbol,
            SynthesizedMember member => member.Replacement,
            _ => null,
        };

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

        return !IsEmittedBase(root.ContainingType, target);
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

    private static bool IsOperator(IMethodSymbol method) =>
        method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion;

    private static bool IsUserMethod(IMethodSymbol method) =>
        method.MethodKind
            is MethodKind.Ordinary
                or MethodKind.UserDefinedOperator
                or MethodKind.Conversion
                or MethodKind.ExplicitInterfaceImplementation;

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
        && IsEmittedBase(implementation.ContainingType, target);

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
            .LastOrDefault(declaration => IsEmittedBase(declaration.ContainingType, policy.Target));

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
            if (!IsTranspiledInterface(implemented, target))
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

    /// <summary>
    /// A transpiled interface whose groups implementers anchor on: emitted
    /// (not <c>[External]</c>) and a real type (not <c>[JsCallable]</c>).
    /// </summary>
    internal static bool IsTranspiledInterface(INamedTypeSymbol type, TargetLanguage? target) =>
        IrTypeRefMapper.IsTranspilableType(type.OriginalDefinition, target)
        && !SymbolHelper.HasExternal(type.OriginalDefinition)
        && !SymbolHelper.HasJsCallable(type.OriginalDefinition);

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
    /// The overloads an emitted base class's dispatcher declares for
    /// <paramref name="name"/>, as <paramref name="type"/> sees them; a
    /// <c>new</c> method of the type hides the base overload of its
    /// signature. <c>null</c> when no base dispatches the name.
    /// </summary>
    internal static IReadOnlyList<GroupMember>? InheritedOverloadsOf(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<GroupMember> declared,
        OverloadGroupingPolicy policy
    )
    {
        // Operators lower to static helpers of their own type, never to an
        // inherited dispatcher.
        if (declared.OfType<DeclaredMember>().Any(member => IsOperator(member.Symbol)))
            return null;

        if (DispatcherOverloadsAbove(type, name, policy) is not { } inherited)
            return null;

        return inherited.Where(overload => !IsHiddenBy(declared, overload)).ToList();
    }

    /// <summary>
    /// Whether a <c>new</c> method of <paramref name="declared"/> hides the
    /// inherited <paramref name="overload"/> of its signature. A synthesized
    /// member is overridden, never hidden.
    /// </summary>
    private static bool IsHiddenBy(IReadOnlyList<GroupMember> declared, GroupMember overload) =>
        overload is DeclaredMember inherited
        && declared
            .OfType<DeclaredMember>()
            .Any(member =>
                !member.Symbol.IsOverride
                && SymbolHelper.HasSameParameters(member.Symbol, inherited.Symbol)
            );

    /// <summary>
    /// The dispatcher overloads the nearest emitted base declaring
    /// <paramref name="name"/> emits, constructed through
    /// <paramref name="type"/>'s base chain: the overloads every level adds,
    /// base-most first. <c>null</c> when that base's group is not
    /// dispatched.
    /// </summary>
    private static IReadOnlyList<GroupMember>? DispatcherOverloadsAbove(
        INamedTypeSymbol type,
        string name,
        OverloadGroupingPolicy policy
    )
    {
        for (
            var ancestor = type.BaseType;
            IsEmittedBase(ancestor, policy.Target);
            ancestor = ancestor!.BaseType
        )
        {
            var group = Of(ancestor!, policy)
                .FirstOrDefault(candidate =>
                    candidate.Name == name && !candidate.IsSegregatedOverrides
                );

            if (group is null)
                continue;

            var added = group
                .Members.Where(member => member.Anchor?.Kind is not IrOverloadAnchorKind.Override)
                .Select(member => ConstructedIn(ancestor!, member))
                .ToList();

            if (group.InheritedOverloads is null)
                return group.Members.Count >= 2 ? added : null;

            var above = (DispatcherOverloadsAbove(ancestor!, name, policy) ?? []).Where(overload =>
                !added.Any(own => HaveSameParameters(own, overload))
            );

            return [.. above, .. added];
        }

        return null;
    }

    private static bool HaveSameParameters(GroupMember member, GroupMember other) =>
        (member, other) switch
        {
            (DeclaredMember declared, DeclaredMember otherDeclared) =>
                SymbolHelper.HasSameParameters(declared.Symbol, otherDeclared.Symbol),
            (SynthesizedMember synthesized, SynthesizedMember otherSynthesized) => synthesized.Kind
                == otherSynthesized.Kind,
            _ => false,
        };

    /// <summary>
    /// A class the target emits, whose dispatched groups a derived class and
    /// a <c>base.M(...)</c> call can reach by fast path: transpiled, not
    /// <c>[External]</c>, not a BCL or special type.
    /// </summary>
    internal static bool IsEmittedBase(INamedTypeSymbol? type, TargetLanguage? target) =>
        type is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None }
        && IrTypeRefMapper.IsTranspilableType(type.OriginalDefinition, target)
        && !SymbolHelper.HasExternal(type.OriginalDefinition);

    /// <summary>
    /// <paramref name="member"/> as the constructed <paramref name="type"/>
    /// declares it: a declared method with its type arguments substituted.
    /// </summary>
    private static GroupMember ConstructedIn(INamedTypeSymbol type, GroupMember member) =>
        member is DeclaredMember declared
            ? declared with
            {
                Symbol = ConstructedIn(type, declared.Symbol),
            }
            : member;

    /// <summary>The member of a constructed type that <paramref name="definition"/> declares.</summary>
    private static IMethodSymbol ConstructedIn(INamedTypeSymbol type, IMethodSymbol definition) =>
        type.GetMembers(definition.Name)
            .OfType<IMethodSymbol>()
            .First(member =>
                SymbolEqualityComparer.Default.Equals(
                    member.OriginalDefinition,
                    definition.OriginalDefinition
                )
            );

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
