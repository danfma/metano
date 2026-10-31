using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

namespace Metano;

/// <summary>
/// <see cref="ITranspilerTarget"/> implementation for the TypeScript backend.
/// Wraps the legacy <see cref="TypeTransformer"/> + <see cref="Printer"/> pipeline.
/// </summary>
/// <remarks>
/// The package.json inputs of the run (generated <see cref="TsSourceFile"/>s, package
/// identity, cross-package dependencies) are exposed via
/// <see cref="LastPackageManifestInputs"/> after a Transform (or AdoptCachedOutput) call so
/// the caller can perform target-specific post-processing (writing a package.json with
/// imports/exports/dependencies, see <see cref="PackageJsonReconciler"/>).
/// </remarks>
public sealed class TypeScriptTarget : ITranspilerTarget
{
    public string Name => "typescript";

    public TargetLanguage Language => TargetLanguage.TypeScript;

    /// <summary>
    /// The package.json inputs captured by the most recent <see cref="Transform"/> call,
    /// or rebuilt by <see cref="AdoptCachedOutput"/> after a full cache hit.
    /// <see cref="PackageManifestInputs.None"/> until either one is invoked.
    /// </summary>
    public PackageManifestInputs LastPackageManifestInputs { get; private set; } =
        PackageManifestInputs.None;

    /// <summary>
    /// When <c>true</c>, <see cref="BarrelFileGenerator"/> emits an
    /// additional <c>src/index.ts</c> root barrel that aggregates every
    /// leaf barrel under nested <c>export namespace</c> blocks mirroring
    /// the C# namespace hierarchy. Opt-in via <c>--namespace-barrels</c>;
    /// the default stays leaf-only so tree-shaking under current
    /// bundlers continues to work without surprises (see ADR-0006).
    /// </summary>
    public bool NamespaceBarrels { get; init; }

    /// <summary>
    /// When <c>true</c>, the transformer rewrites every interface
    /// whose C# name matches <c>^I[A-Z]</c> to drop the leading
    /// <c>I</c> (so <c>IIssueRepository</c> emits as
    /// <c>IssueRepository</c>). Interfaces whose stripped name would
    /// collide with another top-level type in the same namespace
    /// keep the prefix and raise <c>MS0017</c>. Explicit
    /// <c>[Name(TypeScript, "…")]</c> overrides win over the strip.
    /// Opt-in via <c>--strip-interface-prefix</c>; the default stays
    /// off so existing consumer imports keep working on upgrade.
    /// </summary>
    public bool StripInterfacePrefix { get; init; }

    /// <summary>
    /// Optional isolated subpath-imports alias for internal imports. <c>null</c> keeps
    /// the default <c>#</c>. When set (e.g. <c>contracts</c>), generated internal imports
    /// use <c>#contracts/...</c> and the package.json gets only the alias-scoped keys,
    /// leaving a host project's own <c>#</c> untouched. Set via <c>--import-alias</c>.
    /// </summary>
    public string? ImportAlias { get; init; }

    /// <summary>
    /// Cache fingerprint (ADR-0021): pin the emit-affecting flags so
    /// flipping any one invalidates the incremental cache. Format is
    /// kept small + ordered so future additions extend it without
    /// breaking older caches (the format-version bump in
    /// <see cref="TranspilationCache"/> handles structural breaks).
    /// </summary>
    public string ConfigurationFingerprint =>
        BuildConfigurationFingerprint(NamespaceBarrels, StripInterfacePrefix, ImportAlias);

    /// <summary>
    /// Per-target fingerprint pinned into the incremental cache key
    /// (ADR-0021). When the host extends the fingerprint with its own
    /// <c>filePrefix</c>, the host-level helper produces the final
    /// composite — this method covers the target-owned portion only.
    /// </summary>
    private static string BuildConfigurationFingerprint(
        bool namespaceBarrels,
        bool stripInterfacePrefix,
        string? importAlias
    ) =>
        $"layout=full-namespace-v1;namespaceBarrels={namespaceBarrels};stripInterfacePrefix={stripInterfacePrefix};importAlias={PathNaming.NormalizeImportAliasPrefix(importAlias)}";

    /// <summary>
    /// Builds the cache-fingerprint composite consumed by
    /// <see cref="TypeTransformer.CacheConfigurationFingerprint"/>:
    /// target flags + the host's file prefix. Both call sites
    /// (read at construction here and read by the host when
    /// validating an on-disk cache entry) must share this format
    /// verbatim or a flag-flip never invalidates the cache.
    /// </summary>
    private static string BuildCacheFingerprint(string targetFingerprint, string? filePrefix) =>
        $"{targetFingerprint};filePrefix={filePrefix ?? string.Empty}";

    public TargetOutput Transform(
        IrCompilation ir,
        Compilation? compilation,
        string? outputDir = null,
        string? filePrefix = null
    )
    {
        var roslynCompilation = RequireRoslynCompilation(compilation);

        var transformer = new TypeTransformer(ir, roslynCompilation)
        {
            NamespaceBarrels = NamespaceBarrels,
            StripInterfacePrefix = StripInterfacePrefix,
            ImportAlias = ImportAlias,
            CacheOutputDir = outputDir,
            CacheFilePrefix = filePrefix,
            CacheConfigurationFingerprint = BuildCacheFingerprint(
                ConfigurationFingerprint,
                filePrefix
            ),
        };
        var sourceFiles = transformer.TransformAll();

        var crossPackageDependencies = transformer.CrossPackageDependencies;

        LastPackageManifestInputs = CapturePackageManifestInputs(
            ir,
            roslynCompilation,
            sourceFiles,
            crossPackageDependencies
        );

        var printer = new Printer();
        var generated = new List<GeneratedFile>(sourceFiles.Count);
        foreach (var file in sourceFiles)
        {
            // Per-group cache hits (ADR-0023) surface stub TsSourceFiles
            // here so the barrel + cyclic stages see the right
            // imports + exports, but the on-disk bytes already match
            // the previous emit. Skip Printer for those — reuse the
            // disk content directly so the host's emit pass writes
            // the same bytes back.
            if (transformer.CachedFileContents.TryGetValue(file.FileName, out var cached))
                generated.Add(new GeneratedFile(file.FileName, cached));
            else
                generated.Add(new GeneratedFile(file.FileName, printer.Print(file)));
        }

        return new TargetOutput(generated, transformer.Diagnostics)
        {
            PackageDependencies = crossPackageDependencies,
        };
    }

    /// <summary>
    /// Restores the package.json inputs when the host reused the whole previous output
    /// (issues #273, #299). The package identity comes from the IR, exactly as in
    /// <see cref="Transform"/>; the barrel list is rebuilt from the reused file names
    /// (statement-less <see cref="TsSourceFile"/>s, enough for
    /// <see cref="PackageJsonWriter"/>, which reads file names only); the cross-package
    /// dependencies are the ones the producing run returned as
    /// <see cref="TargetOutput.PackageDependencies"/>, restored by the host from its cache.
    /// The package.json stage therefore writes the same manifest as a cold run, even when
    /// package.json was deleted in between. Like <see cref="Transform"/>, it requires the
    /// Roslyn compilation (the <c>[EmitPackage]</c> fallback and the executable check read
    /// it).
    /// </summary>
    public void AdoptCachedOutput(
        IrCompilation ir,
        Compilation? compilation,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlyDictionary<string, string> packageDependencies
    )
    {
        var roslynCompilation = RequireRoslynCompilation(compilation);
        var reusedFiles = files.Select(file => new TsSourceFile(file.RelativePath, [])).ToList();

        LastPackageManifestInputs = CapturePackageManifestInputs(
            ir,
            roslynCompilation,
            reusedFiles,
            packageDependencies
        );
    }

    private static PackageManifestInputs CapturePackageManifestInputs(
        IrCompilation ir,
        Compilation compilation,
        IReadOnlyList<TsSourceFile> sourceFiles,
        IReadOnlyDictionary<string, string> crossPackageDependencies
    ) =>
        new(
            sourceFiles,
            // Prefer the frontend-populated package name; the underlying Roslyn read
            // remains as a defensive fallback while every consumer migrates onto IR.
            PackageName: ir.PackageName
                ?? SymbolHelper.GetEmitPackage(
                    compilation.Assembly,
                    targetEnumValue: (int)EmitTarget.JavaScript
                ),
            SubpathRoot: ir.PackageSubpathRoot,
            CrossPackageDependencies: crossPackageDependencies,
            IsExecutable: compilation.Options.OutputKind == OutputKind.ConsoleApplication
        );

    private static Compilation RequireRoslynCompilation(Compilation? compilation) =>
        compilation
        ?? throw new NotSupportedException(
            "TypeScriptTarget currently requires a Roslyn-backed source frontend; "
                + "compilation was null. The Roslyn dependency will go away once the "
                + "TypeScript transformer reads everything it needs from IrCompilation."
        );
}
