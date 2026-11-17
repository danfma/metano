using Metano.Annotations;
using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The names a target emits a type's synthesized value-equality members under
/// (spec 013 FR-039): the ones its runtime calls. A <c>null</c> name keeps
/// that member out of every group. Plain strings, so the record compares by
/// value and keys the groups' cache.
/// </summary>
/// <param name="EqualsName">The synthesized <c>Equals(object)</c>'s.</param>
/// <param name="HashCodeName">The synthesized <c>GetHashCode()</c>'s.</param>
/// <param name="CopyName">A record's copy behind <c>with</c>.</param>
/// <param name="FastPathSuffix">The suffix pinned on every synthesized member
/// that joins a group (DECIDED-34), the same at every level and package.</param>
public sealed record SynthesizedMemberNames(
    string? EqualsName,
    string? HashCodeName,
    string? CopyName,
    string FastPathSuffix
)
{
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
