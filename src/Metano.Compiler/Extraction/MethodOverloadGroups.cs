using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Metano.Annotations;
using Metano.Compiler.Frontend.Roslyn;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// One method a type declares, as the overload groups see it.
/// </summary>
/// <param name="Symbol">The declared method.</param>
/// <param name="Ordinal">Its position among the methods the type declares
/// under its C# name (<see cref="IrOverloadRef.Ordinal"/>).</param>
/// <param name="Anchor">The root it reuses the names of; <c>null</c> when it
/// is its own root.</param>
internal sealed record MethodGroupMember(IMethodSymbol Symbol, int Ordinal, MethodAnchor? Anchor);

/// <summary>
/// The root declaration a method reuses the names of (spec 013 FR-025).
/// </summary>
/// <param name="Root">The first declaration of its override chain, or the
/// (constructed) interface member it implements.</param>
/// <param name="Kind">Override or implementation.</param>
/// <param name="FurtherRoots">Other dispatched interface members the method
/// implements, in the order of the type's interfaces.</param>
internal sealed record MethodAnchor(
    IMethodSymbol Root,
    IrOverloadAnchorKind Kind,
    IReadOnlyList<IMethodSymbol> FurtherRoots
);

/// <summary>
/// The methods a type declares under one C# name, the way spec 013 groups
/// them for dispatch (DECIDED-15, DECIDED-29).
/// </summary>
/// <param name="Name">The C# name; an explicit implementation's interface
/// member name.</param>
/// <param name="Members">The declared methods, in declaration order.</param>
/// <param name="InheritedOverloads">The overloads of the dispatcher an
/// emitted base class already declares for the name, as this type sees them
/// (type arguments substituted, base-most first); <c>null</c> when no base
/// dispatches it.</param>
/// <param name="IsSegregatedOverrides">The overrides of roots that are not
/// dispatched (an inherited single virtual): they keep their own group, as
/// before spec 013 (DECIDED-29, #159).</param>
internal sealed record MethodOverloadGroup(
    string Name,
    IReadOnlyList<MethodGroupMember> Members,
    IReadOnlyList<IMethodSymbol>? InheritedOverloads,
    bool IsSegregatedOverrides
)
{
    /// <summary>
    /// Whether the group's members are fast paths of a dispatched group: two
    /// or more declared, an inherited dispatcher, or one member anchored on
    /// a dispatched root.
    /// </summary>
    public bool IsDispatched =>
        InheritedOverloads is not null
        || Members.Count >= 2
        || Members.Any(member => member.Anchor is not null);

    public MethodGroupMember? MemberOf(IMethodSymbol method) =>
        Members.FirstOrDefault(member =>
            SymbolEqualityComparer.Default.Equals(
                member.Symbol.OriginalDefinition,
                method.OriginalDefinition
            )
        );
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
        ConcurrentDictionary<TargetLanguage, IReadOnlyList<MethodOverloadGroup>>
    > Cache = new();

    /// <summary>The cache key of an extraction for no particular target.</summary>
    private const TargetLanguage NoTarget = (TargetLanguage)(-1);

    /// <summary>The method groups <paramref name="type"/> declares, in first-declaration order.</summary>
    public static IReadOnlyList<MethodOverloadGroup> Of(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        Cache
            .GetValue(type.OriginalDefinition, static _ => new())
            .GetOrAdd(target ?? NoTarget, _ => Compute(type.OriginalDefinition, target));

    /// <summary>
    /// The suffix of each member of <paramref name="group"/>, declared by
    /// <paramref name="type"/> (FR-001, FR-025): an anchored member's is its
    /// root's; the others are named over the visible group.
    /// </summary>
    public static IReadOnlyDictionary<IMethodSymbol, string> SuffixesOf(
        INamedTypeSymbol type,
        MethodOverloadGroup group,
        TargetLanguage? target
    )
    {
        var suffixes = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);

        foreach (var member in group.Members)
        {
            if (member.Anchor is { } anchor)
                suffixes[member.Symbol] = RootSuffixOf(anchor.Root, target);
        }

        var own = group.Members.Where(member => member.Anchor is null).ToList();

        if (own.Count == 0)
            return suffixes;

        var visible = VisibleInheritedOverloads(type, group)
            .Concat(group.Members.Select(member => member.Symbol))
            .ToList();

        var computed = OverloadGroupNaming.SuffixesOf(visible);
        var offset = visible.Count - group.Members.Count;

        for (var i = 0; i < group.Members.Count; i++)
        {
            if (group.Members[i].Anchor is null)
                suffixes[group.Members[i].Symbol] = computed[offset + i];
        }

        return suffixes;
    }

    /// <summary>
    /// The group of <paramref name="type"/>'s definition that declares
    /// <paramref name="method"/>; <c>null</c> when none does.
    /// </summary>
    public static MethodOverloadGroup? GroupOf(IMethodSymbol method, TargetLanguage? target) =>
        Of(method.ContainingType, target)
            .FirstOrDefault(group => group.MemberOf(method) is not null);

    /// <summary>
    /// The identity of a declared method or interface member
    /// (<see cref="IrOverloadRef"/>): its declaring type, its group name and
    /// its ordinal there.
    /// </summary>
    public static IrOverloadRef RefOf(IMethodSymbol method, TargetLanguage? target)
    {
        var definition = method.OriginalDefinition;
        var type = definition.ContainingType;

        if (type.TypeKind == TypeKind.Interface)
            return new IrOverloadRef(
                IrTypeKeys.Of(type),
                definition.Name,
                InterfaceGroupOf(type, definition.Name, target).IndexOf(definition)
            );

        var member =
            GroupOf(definition, target)?.MemberOf(definition)
            ?? throw new InvalidOperationException(
                $"'{definition.ToDisplayString()}' is not a grouped method of its type."
            );

        return new IrOverloadRef(IrTypeKeys.Of(type), GroupNameOf(definition), member.Ordinal);
    }

    /// <summary>
    /// The public ordinary methods an interface declares under
    /// <paramref name="name"/>, the members of its overload group in
    /// declaration order.
    /// </summary>
    public static List<IMethodSymbol> InterfaceGroupOf(
        INamedTypeSymbol type,
        string name,
        TargetLanguage? target
    ) =>
        type
            .OriginalDefinition.GetMembers(name)
            .OfType<IMethodSymbol>()
            .Where(method => IsInterfaceGroupMethod(method, target))
            .ToList();

    /// <summary>
    /// The name a method groups under: its C# name, or an explicit
    /// implementation's interface member name.
    /// </summary>
    public static string GroupNameOf(IMethodSymbol method) =>
        ExplicitImplementationLowering.ImplementedInterfaceMember(method)?.Name ?? method.Name;

    private static IReadOnlyList<MethodOverloadGroup> Compute(
        INamedTypeSymbol type,
        TargetLanguage? target
    )
    {
        var declared = DeclaredMethods(type, target).ToList();

        return declared
            .GroupBy(GroupNameOf, StringComparer.Ordinal)
            .SelectMany(methods => GroupsNamed(type, methods.Key, methods.ToList(), target))
            .OrderBy(group => declared.IndexOf(group.Members[0].Symbol))
            .ToList();
    }

    /// <summary>
    /// The groups of the methods a type declares under one name: the main
    /// group, and the overrides of roots that are not dispatched apart.
    /// </summary>
    private static IEnumerable<MethodOverloadGroup> GroupsNamed(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<IMethodSymbol> methods,
        TargetLanguage? target
    )
    {
        var members = methods
            .Select(
                (method, ordinal) =>
                    new MethodGroupMember(method, ordinal, AnchorOf(type, method, target))
            )
            .ToList();

        var segregated = members.Where(IsSegregatedOverride).ToList();
        var main = members.Except(segregated).ToList();

        if (main.Count > 0)
        {
            yield return new MethodOverloadGroup(
                name,
                main,
                InheritedOverloadsOf(type, name, main, target),
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

    private static bool IsSegregatedOverride(MethodGroupMember member) =>
        member.Symbol.IsOverride && member.Anchor is null;

    /// <summary>
    /// The methods the class extractor declares, in declaration order:
    /// ordinary methods, operators, conversions and the explicit
    /// implementations it keeps, minus <c>[Ignore]</c>d and inlined ones.
    /// </summary>
    private static IEnumerable<IMethodSymbol> DeclaredMethods(
        INamedTypeSymbol type,
        TargetLanguage? target
    ) =>
        type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => IsDeclaredGroupMethod(method, target));

    private static bool IsDeclaredGroupMethod(IMethodSymbol method, TargetLanguage? target) =>
        !method.IsImplicitlyDeclared
        && !SymbolHelper.HasIgnore(method, target)
        && IsUserMethod(method)
        && !IsDroppedExplicitImplementation(method, target)
        && !SymbolHelper.IsInlinedAtCallSite(method);

    private static bool IsOperator(IMethodSymbol method) =>
        method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion;

    private static bool IsUserMethod(IMethodSymbol method) =>
        method.MethodKind
            is MethodKind.Ordinary
                or MethodKind.UserDefinedOperator
                or MethodKind.Conversion
                or MethodKind.ExplicitInterfaceImplementation;

    private static bool IsDroppedExplicitImplementation(
        IMethodSymbol method,
        TargetLanguage? target
    ) =>
        ExplicitImplementationLowering.Resolve(method, target)
            is { Fate: not ExplicitImplementationFate.EmittedUnderInterfaceName };

    /// <summary>
    /// What <paramref name="method"/> is anchored on: the root of its
    /// override chain when that root's group is dispatched, else the members
    /// of dispatched interface groups it implements; <c>null</c> for its own
    /// root. Static members are never anchored.
    /// </summary>
    private static MethodAnchor? AnchorOf(
        INamedTypeSymbol type,
        IMethodSymbol method,
        TargetLanguage? target
    )
    {
        if (method.IsStatic)
            return null;

        var interfaceRoots = InterfaceRootsOf(type, method, target);

        // A root of an interface the type implements more than once keeps
        // runtime dispatch under its name (DECIDED-30): every implementation
        // anchors on it, an override of a base's included.
        var constructedRoot = interfaceRoots.FirstOrDefault(root =>
            MultiplyConstructedInterfaces.AreMultiplyConstructed(type, root.ContainingType)
        );

        if (constructedRoot is not null)
            return new MethodAnchor(constructedRoot, KindOf(method), []);

        // An override reuses its root's names; the base it overrides already
        // declares the forwarding members of any further interface roots.
        if (method.IsOverride && DispatchedOverrideRootOf(method, target) is { } root)
            return new MethodAnchor(root, IrOverloadAnchorKind.Override, []);

        if (interfaceRoots.Count == 0)
            return null;

        // An interface root is implemented, never overridden, even by an
        // override of an undispatched base virtual: the base declares no fast
        // path under the root's name (the claim pass marks the ones it does).
        return new MethodAnchor(
            interfaceRoots[0],
            IrOverloadAnchorKind.Implementation,
            method.IsOverride ? [] : interfaceRoots.Skip(1).ToList()
        );
    }

    private static IrOverloadAnchorKind KindOf(IMethodSymbol method) =>
        method.IsOverride ? IrOverloadAnchorKind.Override : IrOverloadAnchorKind.Implementation;

    /// <summary>
    /// The first declaration of <paramref name="method"/>'s override chain,
    /// when an emitted class declares it in a dispatched group (DECIDED-29
    /// keeps the others; a BCL root such as <c>object.Equals</c> is not
    /// emitted).
    /// </summary>
    private static IMethodSymbol? DispatchedOverrideRootOf(
        IMethodSymbol method,
        TargetLanguage? target
    )
    {
        var root = method.OriginalDefinition;

        while (root.OverriddenMethod is { } overridden)
            root = overridden.OriginalDefinition;

        if (SymbolEqualityComparer.Default.Equals(root, method.OriginalDefinition))
            return null;

        if (!IsEmittedBase(root.ContainingType, target))
            return null;

        return GroupOf(root, target) is { IsDispatched: true } ? root : null;
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
                if (!IsInterfaceGroupMethod(member, target))
                    continue;

                if (InterfaceGroupOf(implemented, member.Name, target).Count < 2)
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

    private static bool IsInterfaceGroupMethod(IMethodSymbol method, TargetLanguage? target) =>
        method is { MethodKind: MethodKind.Ordinary, IsStatic: false, IsImplicitlyDeclared: false }
        && method.DeclaredAccessibility == Accessibility.Public
        && !SymbolHelper.HasIgnore(method, target);

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
    private static string RootSuffixOf(IMethodSymbol root, TargetLanguage? target)
    {
        using var _ = ExtractionDiagnostics.Suppress();

        var definition = root.OriginalDefinition;
        var type = definition.ContainingType;

        if (type.TypeKind == TypeKind.Interface)
        {
            var members = InterfaceGroupOf(type, definition.Name, target);

            return OverloadGroupNaming.SuffixesOf(members)[members.IndexOf(definition)];
        }

        var group =
            GroupOf(definition, target)
            ?? throw new InvalidOperationException(
                $"'{definition.ToDisplayString()}' is not a grouped method of its type."
            );

        return SuffixesOf(type, group, target)[group.MemberOf(definition)!.Symbol];
    }

    /// <summary>
    /// The overloads an emitted base class's dispatcher declares for
    /// <paramref name="name"/>, as <paramref name="type"/> sees them; a
    /// <c>new</c> method of the type hides the base overload of its
    /// signature. <c>null</c> when no base dispatches the name.
    /// </summary>
    private static IReadOnlyList<IMethodSymbol>? InheritedOverloadsOf(
        INamedTypeSymbol type,
        string name,
        IReadOnlyList<MethodGroupMember> declared,
        TargetLanguage? target
    )
    {
        // Operators lower to static helpers of their own type, never to an
        // inherited dispatcher.
        if (declared.Any(member => IsOperator(member.Symbol)))
            return null;

        if (DispatcherOverloadsAbove(type, name, target) is not { } inherited)
            return null;

        return inherited
            .Where(overload =>
                !declared.Any(member =>
                    !member.Symbol.IsOverride
                    && SymbolHelper.HasSameParameters(member.Symbol, overload)
                )
            )
            .ToList();
    }

    /// <summary>
    /// The dispatcher overloads the nearest emitted base declaring
    /// <paramref name="name"/> emits, constructed through
    /// <paramref name="type"/>'s base chain: the overloads every level adds,
    /// base-most first. <c>null</c> when that base's group is not
    /// dispatched.
    /// </summary>
    private static IReadOnlyList<IMethodSymbol>? DispatcherOverloadsAbove(
        INamedTypeSymbol type,
        string name,
        TargetLanguage? target
    )
    {
        for (
            var ancestor = type.BaseType;
            IsEmittedBase(ancestor, target);
            ancestor = ancestor!.BaseType
        )
        {
            var group = Of(ancestor!, target)
                .FirstOrDefault(candidate =>
                    candidate.Name == name && !candidate.IsSegregatedOverrides
                );

            if (group is null)
                continue;

            var added = group
                .Members.Where(member => member.Anchor?.Kind is not IrOverloadAnchorKind.Override)
                .Select(member => ConstructedIn(ancestor!, member.Symbol))
                .ToList();

            if (group.InheritedOverloads is null)
                return group.Members.Count >= 2 ? added : null;

            var above = (DispatcherOverloadsAbove(ancestor!, name, target) ?? []).Where(overload =>
                !added.Any(own => SymbolHelper.HasSameParameters(own, overload))
            );

            return [.. above, .. added];
        }

        return null;
    }

    /// <summary>
    /// A class the target emits, whose dispatched groups a derived class and
    /// a <c>base.M(...)</c> call can reach by fast path: transpiled, not
    /// <c>[External]</c>, not a BCL or special type.
    /// </summary>
    internal static bool IsEmittedBase(INamedTypeSymbol? type, TargetLanguage? target) =>
        type is { TypeKind: TypeKind.Class, SpecialType: SpecialType.None }
        && IrTypeRefMapper.IsTranspilableType(type.OriginalDefinition, target)
        && !SymbolHelper.HasExternal(type.OriginalDefinition);

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
    ) => (group.InheritedOverloads ?? []).Where(overload => IsVisibleFrom(overload, type));

    private static bool IsVisibleFrom(IMethodSymbol member, INamedTypeSymbol type) =>
        member.DeclaredAccessibility switch
        {
            Accessibility.Private => false,
            Accessibility.Internal or Accessibility.ProtectedAndInternal =>
                member.ContainingAssembly.GivesAccessTo(type.ContainingAssembly),
            _ => true,
        };
}
