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
/// The list of generated <see cref="TsSourceFile"/>s is exposed via <see cref="LastSourceFiles"/>
/// after a Transform call so the caller can perform target-specific post-processing
/// (e.g., writing a package.json with imports/exports/sideEffects derived from the AST).
/// </remarks>
public sealed class TypeScriptTarget : ITranspilerTarget
{
    public string Name => "typescript";

    public TargetLanguage Language => TargetLanguage.TypeScript;

    /// <summary>
    /// The TS AST source files produced by the most recent <see cref="Transform"/> call.
    /// Empty until Transform is invoked.
    /// </summary>
    public IReadOnlyList<TsSourceFile> LastSourceFiles { get; private set; } = [];

    /// <summary>
    /// The package name read from <c>[assembly: EmitPackage(name)]</c> on the compiled
    /// assembly, or null when the attribute isn't present. Used by the CLI driver to
    /// pass an authoritative name to <see cref="PackageJsonWriter"/>.
    /// </summary>
    public string? LastEmitPackageName { get; private set; }

    /// <summary>
    /// Cross-package dependencies inferred from the most recent <see cref="Transform"/>
    /// call: each entry maps a referenced npm package name to its version specifier
    /// (e.g., <c>^1.2.3</c> or <c>workspace:*</c>). The CLI driver merges these into
    /// the consumer's <c>package.json#dependencies</c> so the user doesn't have to
    /// manually track which sibling packages their code imports.
    /// </summary>
    public IReadOnlyDictionary<string, string> LastCrossPackageDependencies { get; private set; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Whether the source project is an executable (ConsoleApplication). Executables
    /// don't need <c>package.json#exports</c> because they're not consumed by other
    /// packages — only <c>imports</c> (for internal barrel references and tests).
    /// </summary>
    public bool LastIsExecutable { get; private set; }

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
        if (compilation is null)
            throw new NotSupportedException(
                "TypeScriptTarget currently requires a Roslyn-backed source frontend; "
                    + "compilation was null. The Roslyn dependency will go away once the "
                    + "TypeScript transformer reads everything it needs from IrCompilation."
            );

        var transformer = new TypeTransformer(ir, compilation)
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
        LastSourceFiles = sourceFiles;
        // Prefer the frontend-populated package name; the underlying Roslyn read
        // remains as a defensive fallback while every consumer migrates onto IR.
        LastEmitPackageName =
            ir.PackageName
            ?? SymbolHelper.GetEmitPackage(
                compilation.Assembly,
                targetEnumValue: (int)EmitTarget.JavaScript
            );
        LastCrossPackageDependencies = transformer.CrossPackageDependencies;
        LastIsExecutable = compilation.Options.OutputKind == OutputKind.ConsoleApplication;

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

        return new TargetOutput(generated, transformer.Diagnostics);
    }
}
