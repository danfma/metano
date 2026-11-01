using System.Diagnostics.CodeAnalysis;
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
    /// output. A refused <paramref name="packageRootResolution"/> (no root inside the
    /// workspace, see <see cref="PackageRootResolver"/>) writes nothing: it is reported
    /// as <see cref="PackageJsonReconciliation.Refused"/> when files were produced, and
    /// skipped otherwise, like any absent package.json.
    /// </summary>
    public static PackageJsonReconciliation Reconcile(
        PackageManifestInputs inputs,
        PackageRootResolution packageRootResolution,
        string outputDir,
        string dist,
        string? srcRoot,
        string? importAlias
    )
    {
        var hasFilesToExpose = inputs.SourceFiles.Count > 0;

        if (packageRootResolution.IsRefused)
            return hasFilesToExpose
                ? PackageJsonReconciliation.Refused(packageRootResolution.Refusal)
                : PackageJsonReconciliation.Skipped;

        var packageRoot = packageRootResolution.Root;
        var packageJsonPath = Path.Combine(packageRoot, "package.json");
        var packageJsonExists = File.Exists(packageJsonPath);

        if (!hasFilesToExpose && !packageJsonExists)
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

        return PackageJsonReconciliation.Written(packageJsonPath, diagnostics);
    }
}

/// <summary>
/// The outcome of <see cref="PackageJsonReconciler.Reconcile"/>: whether package.json was
/// written, and the diagnostics of the stage (the writer's when it was written, the
/// refusal's error when it was refused).
/// </summary>
public sealed class PackageJsonReconciliation
{
    /// <summary>The stage did not run: nothing was produced and no package.json exists.</summary>
    public static PackageJsonReconciliation Skipped { get; } = new(null, []);

    private PackageJsonReconciliation(
        string? packageJsonPath,
        IReadOnlyList<MetanoDiagnostic> diagnostics
    )
    {
        PackageJsonPath = packageJsonPath;
        Diagnostics = diagnostics;
    }

    [MemberNotNullWhen(true, nameof(PackageJsonPath))]
    public bool WasWritten => PackageJsonPath is not null;

    /// <summary>The package.json the stage wrote; <c>null</c> when nothing was written.</summary>
    public string? PackageJsonPath { get; }

    public IReadOnlyList<MetanoDiagnostic> Diagnostics { get; }

    /// <summary>Whether the stage reported an error, so the run must fail.</summary>
    public bool HasErrors =>
        Diagnostics.Any(diagnostic => diagnostic.Severity == MetanoDiagnosticSeverity.Error);

    public static PackageJsonReconciliation Written(
        string packageJsonPath,
        IReadOnlyList<MetanoDiagnostic> diagnostics
    ) => new(packageJsonPath, diagnostics);

    /// <summary>No package root inside the workspace: nothing was written.</summary>
    public static PackageJsonReconciliation Refused(MetanoDiagnostic refusal) =>
        new(null, [refusal]);
}
