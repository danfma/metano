using Metano.Compiler.Diagnostics;

namespace Metano;

/// <summary>
/// The post-emit package.json stage of a TypeScript run: feeds the
/// <see cref="PackageManifestInputs"/> the <see cref="TypeScriptTarget"/> captured (from
/// <see cref="TypeScriptTarget.Transform"/>, or from
/// <see cref="TypeScriptTarget.AdoptCachedOutput"/> on a full cache hit) into
/// <see cref="PackageJsonWriter"/>, so the generated barrels are exposed via subpath
/// imports/exports.
/// </summary>
public static class PackageJsonReconciler
{
    /// <summary>
    /// Writes or merges <c>&lt;packageRoot&gt;/package.json</c>. Runs when the target
    /// produced files, OR when nothing was produced but a package.json already exists —
    /// the latter prunes stale transpiler exports after the last transpilable type is
    /// removed, without scaffolding a package.json for a project that never produced
    /// output.
    /// </summary>
    public static PackageJsonReconciliation Reconcile(
        PackageManifestInputs inputs,
        string packageRoot,
        string outputDir,
        string dist,
        string? srcRoot,
        string? importAlias
    )
    {
        var packageJsonExists = File.Exists(Path.Combine(packageRoot, "package.json"));

        if (inputs.SourceFiles.Count == 0 && !packageJsonExists)
            return PackageJsonReconciliation.Skipped;

        var diagnostics = PackageJsonWriter.UpdateOrCreate(
            packageRoot,
            outputDir,
            inputs.SourceFiles,
            dist,
            authoritativePackageName: inputs.PackageName,
            crossPackageDependencies: inputs.CrossPackageDependencies,
            isExecutable: inputs.IsExecutable,
            srcRoot: srcRoot,
            importAlias: importAlias,
            subpathRoot: inputs.SubpathRoot
        );

        return PackageJsonReconciliation.Written(diagnostics);
    }
}

/// <summary>
/// The outcome of <see cref="PackageJsonReconciler.Reconcile"/>: whether package.json was
/// written, and the writer's diagnostics when it was.
/// </summary>
public sealed class PackageJsonReconciliation
{
    /// <summary>The stage did not run: nothing was produced and no package.json exists.</summary>
    public static PackageJsonReconciliation Skipped { get; } = new(false, []);

    private PackageJsonReconciliation(bool wasWritten, IReadOnlyList<MetanoDiagnostic> diagnostics)
    {
        WasWritten = wasWritten;
        Diagnostics = diagnostics;
    }

    public bool WasWritten { get; }

    public IReadOnlyList<MetanoDiagnostic> Diagnostics { get; }

    public static PackageJsonReconciliation Written(IReadOnlyList<MetanoDiagnostic> diagnostics) =>
        new(true, diagnostics);
}
