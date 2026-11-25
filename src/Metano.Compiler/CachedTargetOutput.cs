namespace Metano.Compiler;

/// <summary>
/// What the host's incremental cache restores of a previous run on a full cache hit
/// (ADR-0021), handed to <see cref="ITranspilerTarget.AdoptCachedOutput"/> instead of
/// running <see cref="ITranspilerTarget.Transform"/>. A new piece of restored state
/// becomes a property here, so the target contract does not grow a parameter per fact.
/// </summary>
/// <param name="Files">The reused generated files only, read back from disk; copied
/// assets are not included.</param>
/// <param name="PackageDependencies">The <see cref="TargetOutput.PackageDependencies"/>
/// of the run that produced <paramref name="Files"/>.</param>
/// <param name="ModuleSideEffectFiles">The <see cref="TargetOutput.ModuleSideEffectFiles"/>
/// of the run that produced <paramref name="Files"/>.</param>
/// <param name="Diagnostics">The non-error diagnostics the target and emit stage reported when
/// they produced <paramref name="Files"/>; the host replays them (#785).</param>
public sealed record CachedTargetOutput(
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyDictionary<string, string> PackageDependencies,
    IReadOnlyList<string> ModuleSideEffectFiles,
    IReadOnlyList<Metano.Compiler.Diagnostics.MetanoDiagnostic> Diagnostics
);
