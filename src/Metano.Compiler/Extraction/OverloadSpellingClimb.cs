using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Passes 2 and 3 of <see cref="OverloadNamePolicy"/> (spec 013 FR-003,
/// FR-024; contracts/naming.md § Stage 1, steps 3–6): spells each overload by
/// its parameter types, then, while overloads share a suffix, qualifies the
/// named types of every colliding set one level of their own qualifier chains
/// (containing types, namespaces, <c>Global</c>). Overloads that do not collide
/// keep their spelling. A part every overload of the set shares (the same
/// declaration at the same position: <c>List</c> in <c>List&lt;A.Id&gt;</c> /
/// <c>List&lt;B.Id&gt;</c>) cannot tell them apart, so it does not climb. A set
/// no part of which can climb further is a dead end: its overloads keep their
/// spelling plus a positional marker (<c>$1</c>, <c>$2</c>) in declaration
/// order. One instance runs one climb and is discarded: its state is the
/// climb's progress.
/// </summary>
internal sealed class OverloadSpellingClimb
{
    private readonly IReadOnlyList<IReadOnlyList<IrNamingComponent>> _partsByOverload;
    private readonly int[][] _climbLevels;
    private readonly string?[] _positionalMarkers;
    private readonly List<IReadOnlyList<int>> _deadEnds = [];

    private OverloadSpellingClimb(IReadOnlyList<IReadOnlyList<IrNamingComponent>> components)
    {
        _partsByOverload = components;
        _climbLevels = components.Select(parts => new int[parts.Count]).ToArray();
        _positionalMarkers = new string?[components.Count];
    }

    /// <param name="components">Each overload's spelling parts, its parameters'
    /// parts concatenated, in declaration order.</param>
    public static OverloadSuffixes Run(
        IReadOnlyList<IReadOnlyList<IrNamingComponent>> components
    ) => new OverloadSpellingClimb(components).Run();

    private OverloadSuffixes Run()
    {
        var spellings = Spellings();
        var collisions = CollidingSets(spellings);

        while (collisions.Count > 0)
        {
            foreach (var set in collisions)
                ClimbOrMarkDeadEnd(set);

            spellings = Spellings();
            collisions = CollidingSets(spellings);
        }

        var suffixes = spellings
            .Select((spelling, index) => spelling + _positionalMarkers[index])
            .ToList();

        return new OverloadSuffixes(suffixes, _deadEnds);
    }

    private List<string> Spellings() =>
        _partsByOverload
            .Select(
                (parts, overload) =>
                    string.Concat(
                        parts.Select(
                            (part, partIndex) => part.SpellAt(_climbLevels[overload][partIndex])
                        )
                    )
            )
            .ToList();

    /// <summary>
    /// The sets of unmarked overloads sharing a spelling, each in declaration
    /// order, ordered by their first overload.
    /// </summary>
    private List<List<int>> CollidingSets(IReadOnlyList<string> spellings) =>
        Enumerable
            .Range(0, spellings.Count)
            .Where(overload => _positionalMarkers[overload] is null)
            .GroupBy(overload => spellings[overload], StringComparer.Ordinal)
            .Where(set => set.Count() > 1)
            .Select(set => set.ToList())
            .ToList();

    private void ClimbOrMarkDeadEnd(IReadOnlyList<int> set)
    {
        var climbed = false;

        foreach (var overload in set)
            climbed |= ClimbDistinguishingParts(overload, set);

        if (climbed)
            return;

        for (var position = 0; position < set.Count; position++)
            _positionalMarkers[set[position]] = $"${position + 1}";

        _deadEnds.Add(set);
    }

    private bool ClimbDistinguishingParts(int overload, IReadOnlyList<int> set)
    {
        var parts = _partsByOverload[overload];
        var levels = _climbLevels[overload];
        var climbed = false;

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            if (!CanDistinguishBy(overload, partIndex, set))
                continue;

            levels[partIndex]++;
            climbed = true;
        }

        return climbed;
    }

    /// <summary>
    /// Whether climbing the part can still tell the set's overloads apart: it
    /// has a qualifier left, and not every overload of the set names the same
    /// declaration there.
    /// </summary>
    private bool CanDistinguishBy(int overload, int partIndex, IReadOnlyList<int> set) =>
        _partsByOverload[overload][partIndex].CanClimbFrom(_climbLevels[overload][partIndex])
        && !IsSharedBySet(partIndex, set);

    private bool IsSharedBySet(int partIndex, IReadOnlyList<int> set)
    {
        var first = _partsByOverload[set[0]];

        return set.All(overload =>
            _partsByOverload[overload].Count == first.Count
            && _partsByOverload[overload][partIndex].NamesSameTypeAs(first[partIndex])
        );
    }
}
