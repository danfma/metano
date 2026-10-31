using Metano.Compiler.Diagnostics;

namespace Metano;

/// <summary>
/// The post-emit package.json stage of a TypeScript run: feeds the per-run state the
/// <see cref="TypeScriptTarget"/> captured (from <see cref="TypeScriptTarget.Transform"/>,
/// or from <see cref="TypeScriptTarget.AdoptCachedOutput"/> on a full cache hit) into
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
    /// <returns>The writer's diagnostics, or <see langword="null"/> when the stage was
    /// skipped and no package.json was touched.</returns>
    public static IReadOnlyList<MetanoDiagnostic>? Reconcile(
        TypeScriptTarget target,
        string packageRoot,
        string outputDir,
        string dist,
        string? srcRoot,
        string? importAlias
    )
    {
        var packageJsonExists = File.Exists(Path.Combine(packageRoot, "package.json"));

        if (target.LastSourceFiles.Count == 0 && !packageJsonExists)
            return null;

        return PackageJsonWriter.UpdateOrCreate(
            packageRoot,
            outputDir,
            target.LastSourceFiles,
            dist,
            authoritativePackageName: target.LastEmitPackageName,
            crossPackageDependencies: target.LastCrossPackageDependencies,
            isExecutable: target.LastIsExecutable,
            srcRoot: srcRoot,
            importAlias: importAlias,
            subpathRoot: target.LastPackageSubpathRoot
        );
    }
}
