using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Caching;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// What the transform of every group produced, indexed like the groups so the
/// caller keeps source order after the parallel pass.
/// </summary>
internal sealed class GroupOutcomes(int count)
{
    public TsSourceFile?[] Files { get; } = new TsSourceFile?[count];

    public GroupEmission[] Emissions { get; } = new GroupEmission[count];

    /// <summary>The local classes each transformed group's casts check; <c>null</c> for a group reused from the cache.</summary>
    public IReadOnlySet<string>?[] CheckedCasts { get; } = new IReadOnlySet<string>?[count];
}

/// <summary>
/// What a group contributes to the per-group cache: the metadata of the file it
/// emitted and the cross-package dependencies that file requires.
/// <see cref="IsCacheable"/> is <c>false</c> for a group whose output depends on
/// this run's import graph (<see cref="ImportCycleCastPass"/>).
/// </summary>
internal sealed record GroupEmission(
    IReadOnlyList<CachedFileMetadata> Files,
    IReadOnlyDictionary<string, string> Dependencies,
    bool IsCacheable = true
)
{
    public static GroupEmission Empty { get; } =
        new([], new Dictionary<string, string>(StringComparer.Ordinal));
}
