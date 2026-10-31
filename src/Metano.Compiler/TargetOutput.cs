using Metano.Compiler.Diagnostics;

namespace Metano.Compiler;

/// <summary>
/// The result of running a transpiler target: a list of files to emit and any diagnostics
/// collected during transformation.
/// </summary>
public sealed record TargetOutput(
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<MetanoDiagnostic> Diagnostics
)
{
    /// <summary>
    /// The external packages the generated files depend on, keyed by package name with
    /// the target's version specifier (e.g. npm's <c>^1.2.3</c> or <c>workspace:*</c>).
    /// The host persists it in the incremental cache and hands it back through
    /// <see cref="ITranspilerTarget.AdoptCachedOutput"/> on a full cache hit, so a
    /// target's package manifest stage sees the same dependencies without transforming.
    /// Empty for targets that do not track dependencies.
    /// </summary>
    public IReadOnlyDictionary<string, string> PackageDependencies { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
