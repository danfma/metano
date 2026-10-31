using Metano.Annotations;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// What becomes of an explicit interface implementation (<c>string IPlain.Name =&gt; …</c>)
/// once lowered to a target that has no explicit implementations (TypeScript, Dart).
/// </summary>
public enum ExplicitImplementationFate
{
    /// <summary>
    /// The implemented interface (or the interface member) is <c>[Ignore]</c> for the
    /// target, so it is erased from the heritage clause (#247). The member is only
    /// reachable through that interface, and any reference to the interface already
    /// raises <c>MS0013</c>, so nothing in the output can call it: it is dropped.
    /// </summary>
    Erased,

    /// <summary>
    /// The member implements an interface operator or conversion (a <c>static
    /// abstract</c> generic-math member). Operators lower at their call sites, so
    /// there is no member to declare: it is dropped.
    /// </summary>
    LoweredAtCallSite,

    /// <summary>
    /// The member is declared under the interface member's own name (and
    /// <c>[Name]</c> override), which is the name every call site through the
    /// interface type already emits.
    /// </summary>
    EmittedUnderInterfaceName,

    /// <summary>
    /// The interface is outside the transpiled contract (a BCL or other metadata
    /// interface that is neither <c>[Transpile]</c> nor in a <c>[TranspileAssembly]</c>
    /// assembly), and another member already claims its name — the idiomatic
    /// <c>IEnumerator IEnumerable.GetEnumerator() =&gt; GetEnumerator();</c> bridge.
    /// The bridge only forwards to the member that keeps the name, so it is dropped
    /// silently, as explicit methods always were before #278.
    /// </summary>
    Superseded,

    /// <summary>
    /// The interface belongs to the transpiled contract and another emitted member of
    /// the type (or of a base class) already claims the interface member's name with
    /// the same static-ness, so declaring it would either collide or silently reroute
    /// calls through the interface. It is dropped and the frontend reports
    /// <c>MS0031</c>.
    /// </summary>
    Conflicting,
}

/// <summary>
/// The lowering decided for one explicit interface implementation.
/// </summary>
/// <param name="Member">The explicit implementation on the class.</param>
/// <param name="InterfaceMember">The interface member it implements.</param>
/// <param name="Fate">What the extractor does with it.</param>
/// <param name="ClaimingMember">For <see cref="ExplicitImplementationFate.Conflicting"/>
/// and <see cref="ExplicitImplementationFate.Superseded"/>, the member that already
/// claims the name.</param>
public sealed record ExplicitImplementation(
    ISymbol Member,
    ISymbol InterfaceMember,
    ExplicitImplementationFate Fate,
    ISymbol? ClaimingMember = null
);

/// <summary>
/// Decides how an explicit interface implementation is lowered. The class extractor
/// uses it to drop or rename the member and the frontend validator uses it to report
/// <c>MS0031</c>, so both agree on the same decision. It reads Roslyn symbols only and
/// is target-agnostic.
/// <para>
/// The conflict check compares emitted names (the <c>[Name]</c> override for the
/// target, otherwise the C# name) case-insensitively. That is a conservative premise:
/// every current target camelCases member names, so <c>Name</c> and <c>name</c>
/// collide, and a pair that would not collide after casing (<c>Url</c>/<c>URL</c>) is
/// still reported. Renames a target applies through declarative BCL mappings are not
/// considered. The check scans the type and its base classes, excluding
/// <c>System.Object</c>. Two explicit implementations of members of the same interface
/// (overloads such as <c>IParser.Parse(int)</c> and <c>IParser.Parse(string)</c>)
/// don't conflict; neither does a re-implementation of the same interface in a derived
/// class.
/// </para>
/// </summary>
public static class ExplicitImplementationLowering
{
    /// <summary>
    /// The lowering of <paramref name="member"/>, or <c>null</c> when it is not an
    /// explicit interface implementation.
    /// </summary>
    public static ExplicitImplementation? Resolve(ISymbol member, TargetLanguage? target)
    {
        var interfaceMember = ImplementedInterfaceMember(member);

        if (interfaceMember is null)
            return null;

        if (DropFate(interfaceMember, target) is { } dropped)
            return new(member, interfaceMember, dropped);

        var claiming = FindClaimingMember(member, interfaceMember, target);
        var fate = FateFor(claiming, interfaceMember.ContainingType);

        return new(member, interfaceMember, fate, claiming);
    }

    /// <summary>
    /// The interface member <paramref name="member"/> explicitly implements, or
    /// <c>null</c>. C# allows a single target per explicit implementation. Accessors of
    /// explicit properties and events are not explicit implementations here: their
    /// property or event is.
    /// </summary>
    public static ISymbol? ImplementedInterfaceMember(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => property.ExplicitInterfaceImplementations.FirstOrDefault(),
            IMethodSymbol { MethodKind: MethodKind.ExplicitInterfaceImplementation } method =>
                method.ExplicitInterfaceImplementations.FirstOrDefault(),
            IEventSymbol evt => evt.ExplicitInterfaceImplementations.FirstOrDefault(),
            _ => null,
        };

    /// <summary>
    /// The fate of an explicit implementation the target never declares, whatever the
    /// rest of the type looks like, or <c>null</c> when it is declarable.
    /// </summary>
    private static ExplicitImplementationFate? DropFate(
        ISymbol interfaceMember,
        TargetLanguage? target
    )
    {
        if (IsErased(interfaceMember, target))
            return ExplicitImplementationFate.Erased;

        if (IsOperatorOrConversion(interfaceMember))
            return ExplicitImplementationFate.LoweredAtCallSite;

        return null;
    }

    private static bool IsErased(ISymbol interfaceMember, TargetLanguage? target) =>
        SymbolHelper.HasIgnore(interfaceMember, target)
        || SymbolHelper.HasIgnore(interfaceMember.ContainingType, target);

    private static bool IsOperatorOrConversion(ISymbol interfaceMember) =>
        interfaceMember
            is IMethodSymbol
            {
                MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion
            };

    /// <summary>
    /// An interface the user's transpiled code owns: declared in source, marked
    /// <c>[Transpile]</c>, or shipped by a <c>[TranspileAssembly]</c> package.
    /// </summary>
    private static bool IsInTranspiledContract(INamedTypeSymbol interfaceType)
    {
        var definition = interfaceType.OriginalDefinition;

        return definition.DeclaringSyntaxReferences.Length > 0
            || SymbolHelper.HasTranspile(definition)
            || definition.ContainingAssembly?.HasTranspileAssembly() == true;
    }

    /// <summary>
    /// Declarable under the interface name when nothing claims it; otherwise a
    /// conflict for interfaces of the transpiled contract and a silent drop for the
    /// rest.
    /// </summary>
    private static ExplicitImplementationFate FateFor(
        ISymbol? claiming,
        INamedTypeSymbol interfaceType
    )
    {
        if (claiming is null)
            return ExplicitImplementationFate.EmittedUnderInterfaceName;

        return IsInTranspiledContract(interfaceType)
            ? ExplicitImplementationFate.Conflicting
            : ExplicitImplementationFate.Superseded;
    }

    private static ISymbol? FindClaimingMember(
        ISymbol member,
        ISymbol interfaceMember,
        TargetLanguage? target
    )
    {
        var name = EmittedName(interfaceMember, target);

        return HierarchyMembers(member.ContainingType)
            .Where(other => !SymbolEqualityComparer.Default.Equals(other, member))
            .Where(other => other.IsStatic == member.IsStatic)
            .Where(other => IsDeclaredMember(other, target))
            .FirstOrDefault(other =>
                ClaimsNameOutsideInterface(other, name, interfaceMember.ContainingType, target)
            );
    }

    private static bool ClaimsNameOutsideInterface(
        ISymbol other,
        string name,
        INamedTypeSymbol interfaceType,
        TargetLanguage? target
    ) => ClaimsEmittedName(other, name, target) && !ImplementsSameInterface(other, interfaceType);

    private static bool ClaimsEmittedName(ISymbol other, string name, TargetLanguage? target)
    {
        var otherName = EmittedName(ImplementedInterfaceMember(other) ?? other, target);

        return string.Equals(otherName, name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ImplementsSameInterface(ISymbol other, INamedTypeSymbol interfaceType) =>
        ImplementedInterfaceMember(other) is { } otherInterfaceMember
        && SymbolEqualityComparer.Default.Equals(
            otherInterfaceMember.ContainingType,
            interfaceType
        );

    private static string EmittedName(ISymbol symbol, TargetLanguage? target) =>
        SymbolHelper.GetNameOverride(symbol, target) ?? symbol.Name;

    private static IEnumerable<ISymbol> HierarchyMembers(INamedTypeSymbol type)
    {
        for (
            var current = type;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            foreach (var member in current.GetMembers())
                yield return member;
        }
    }

    /// <summary>
    /// Shape-based: a member the class extractor declares on the target type — fields
    /// that are not property backing fields, properties, events, ordinary methods, and
    /// explicit implementations not dropped by <see cref="DropFate"/>. Members that are
    /// implicitly declared, <c>[Ignore]</c>, or inlined at their call sites
    /// (<see cref="SymbolHelper.IsInlinedAtCallSite"/>) claim no name. An explicit
    /// implementation that is itself in conflict still claims its name, so both sides
    /// of a clash are reported.
    /// </summary>
    private static bool IsDeclaredMember(ISymbol member, TargetLanguage? target)
    {
        if (member.IsImplicitlyDeclared || SymbolHelper.HasIgnore(member, target))
            return false;

        return member switch
        {
            IFieldSymbol field => field.AssociatedSymbol is null,
            IPropertySymbol or IEventSymbol => !IsDroppedExplicit(member, target),
            IMethodSymbol { MethodKind: MethodKind.Ordinary } method =>
                !method.IsInlinedAtCallSite(),
            IMethodSymbol { MethodKind: MethodKind.ExplicitInterfaceImplementation } method =>
                !IsDroppedExplicit(method, target),
            _ => false,
        };
    }

    private static bool IsDroppedExplicit(ISymbol member, TargetLanguage? target) =>
        ImplementedInterfaceMember(member) is { } interfaceMember
        && DropFate(interfaceMember, target) is not null;
}
