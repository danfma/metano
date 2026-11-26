using Metano.Annotations;
using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The names a target emits a type's synthesized value-equality members under
/// (spec 013 FR-039): the ones its runtime calls. A <c>null</c> name keeps
/// that member out of every group. Plain strings, so the record compares by
/// value and keys the groups' cache. Its members are set only at construction,
/// so a <c>with</c> expression cannot break the invariant it checks there.
/// </summary>
public sealed record SynthesizedMemberNames
{
    /// <param name="equalsName">The synthesized <c>Equals(object)</c>'s.</param>
    /// <param name="hashCodeName">The synthesized <c>GetHashCode()</c>'s.</param>
    /// <param name="copyName">A record's copy behind <c>with</c>.</param>
    /// <param name="fastPathSuffix">The suffix pinned on every synthesized member
    /// that joins a group (DECIDED-34), the same at every level and package.</param>
    /// <param name="groupsEquals">Whether the target groups the synthesized
    /// <c>Equals</c> with the user's <c>Equals</c> overloads, which needs
    /// <paramref name="equalsName"/>.</param>
    public SynthesizedMemberNames(
        string? equalsName,
        string? hashCodeName,
        string? copyName,
        string fastPathSuffix,
        bool groupsEquals = false
    )
    {
        if (groupsEquals && equalsName is null)
        {
            throw new ArgumentException(
                "A target that groups the synthesized Equals must name it.",
                nameof(groupsEquals)
            );
        }

        EqualsName = equalsName;
        HashCodeName = hashCodeName;
        CopyName = copyName;
        FastPathSuffix = fastPathSuffix;
        GroupsEquals = groupsEquals;
    }

    /// <summary>The synthesized <c>Equals(object)</c>'s name.</summary>
    public string? EqualsName { get; }

    /// <summary>The synthesized <c>GetHashCode()</c>'s name.</summary>
    public string? HashCodeName { get; }

    /// <summary>The name of a record's copy behind <c>with</c>.</summary>
    public string? CopyName { get; }

    /// <summary>The suffix pinned on every synthesized member that joins a group (DECIDED-34).</summary>
    public string FastPathSuffix { get; }

    /// <summary>
    /// Whether the target groups the synthesized <c>Equals</c> with the user's
    /// <c>Equals</c> overloads (spec 013 FR-045); always named.
    /// </summary>
    public bool GroupsEquals { get; }

    /// <summary>The name <paramref name="member"/> is emitted under; <c>null</c> when the target supplies none.</summary>
    public string? NameOf(IrSynthesizedValueMember member) =>
        member switch
        {
            IrSynthesizedValueMember.Equals => EqualsName,
            IrSynthesizedValueMember.HashCode => HashCodeName,
            _ => CopyName,
        };
}

/// <summary>
/// What the overload groups of a type depend on besides its symbols (spec 013
/// FR-039): the target, and the names it emits synthesized value-equality
/// members under. Each target holds one instance; the extraction context
/// exposes it (<see cref="IrExtractionContext.GroupingPolicy"/>).
/// </summary>
/// <param name="Target">The language target; <c>null</c> for none.</param>
/// <param name="ValueEqualityNames">The target's synthesized member names;
/// <c>null</c> for a target that supplies none, whose groups hold declared
/// methods only.</param>
public sealed record OverloadGroupingPolicy(
    TargetLanguage? Target,
    SynthesizedMemberNames? ValueEqualityNames
)
{
    /// <summary>
    /// Whether the target groups the synthesized <c>Equals</c> with the user's
    /// <c>Equals</c> overloads: it joins a user group, and a struct's explicit
    /// <c>IEquatable&lt;T&gt;.Equals</c> superseded only for its name is kept
    /// (spec 013 FR-045). The capability its names state
    /// (<see cref="SynthesizedMemberNames.GroupsEquals"/>).
    /// </summary>
    public bool GroupsSynthesizedEquals => ValueEqualityNames?.GroupsEquals is true;

    /// <summary>
    /// Whether the target tags or hides <c>internal</c> members from its public
    /// surface (TypeScript: <c>/** @internal */</c> and <c>stripInternal</c>), so a
    /// member that a public interface relies on must not be widened for a base class
    /// that other types inherit (#821). <c>false</c>, the default and the answer for a
    /// <c>null</c> target, for a target that ignores the accessibility of members
    /// (Dart): its output keeps the widened member and the dropped forwarder.
    /// </summary>
    public bool HidesInternalMembers { get; init; }

    /// <summary>The policy of a target that supplies no synthesized member names.</summary>
    public static OverloadGroupingPolicy For(TargetLanguage? target) => new(target, null);

    /// <summary>The name <paramref name="member"/> joins a group under; <c>null</c> when it joins none.</summary>
    public string? NameOf(IrSynthesizedValueMember member) => ValueEqualityNames?.NameOf(member);

    /// <summary>
    /// The stable fast path of <paramref name="member"/> once it joined a group
    /// (DECIDED-34): its name and the pinned suffix; <c>null</c> when it joins none.
    /// </summary>
    public string? FastPathNameOf(IrSynthesizedValueMember member) =>
        ValueEqualityNames is { } names && names.NameOf(member) is { } name
            ? name + names.FastPathSuffix
            : null;
}
