using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

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
    /// The typed replacing <c>Equals</c> this group's synthesized member routes to
    /// although a <c>[Name]</c> keeps it in another group (FR-039): the class
    /// extractor reports it as an error. <c>null</c> for every other group.
    /// </summary>
    public IMethodSymbol? DetachedReplacement { get; init; }

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
