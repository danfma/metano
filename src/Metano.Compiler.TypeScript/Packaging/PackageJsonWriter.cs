using System.Text.Json;
using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Packaging;

/// <summary>
/// Generates and updates the consumer's package.json with <c>exports</c>, <c>sideEffects</c>,
/// <c>type</c> and (only with an import alias) <c>imports</c> fields based on the TypeScript
/// files emitted by the transpiler.
///
/// Strategy: non-destructive merge. The user's hand-written fields (name, dependencies,
/// scripts, etc.) are preserved. Transpiler-managed entries inside <c>imports</c> and
/// <c>exports</c> are merged into the existing objects — user-defined entries for other
/// subpaths survive untouched.
/// </summary>
public static class PackageJsonWriter
{
    internal const string PackageJsonFileName = "package.json";

    private const string PeerDependenciesSection = "peerDependencies";
    private const string PeerDependenciesMetaSection = "peerDependenciesMeta";

    /// <summary>The import condition Bun resolves first, mapped to the <c>.ts</c> sources.</summary>
    private const string BunCondition = "bun";

    /// <summary>
    /// The prefix <see cref="PathNaming.NormalizeImportAliasPrefix"/> gives when no alias is set.
    /// </summary>
    private const string UnaliasedPrefix = "#";

    private static readonly string[] DependencySections =
    [
        "dependencies",
        "devDependencies",
        PeerDependenciesSection,
        "optionalDependencies",
    ];

    /// <summary>The wildcard tails an import target ends with: <c>./lib/*.js</c> or <c>./lib/*</c>.</summary>
    private static readonly string[] CompiledTreeSuffixes = ["/*.js", "/*"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Updates (or creates) the package.json at <paramref name="packageRoot"/> with imports/exports
    /// derived from the generated TS files. Paths are emitted relative to the package root and
    /// point to the dist directory (compiled JS) with a fallback to the source TS files.
    /// </summary>
    /// <param name="packageRoot">Root directory of the consumer Bun/Node package.</param>
    /// <param name="outputDirAbsolute">Absolute path where TS files are emitted (e.g., /path/to/pkg/src).</param>
    /// <param name="files">All generated TsSourceFile objects (type files + barrels).</param>
    /// <param name="distDirRelativeToPackageRoot">Relative path from packageRoot to the JS output
    /// directory (default: "./dist").</param>
    /// <param name="authoritativePackageName">When non-null, this name (typically read
    /// from <c>[assembly: EmitPackage(...)]</c>) is written to <c>package.json#name</c>
    /// as the source of truth.</param>
    /// <param name="crossPackageDependencies">Maps each cross-package npm name to its version
    /// specifier. Merged into <c>package.json#dependencies</c> — user entries for other
    /// packages are preserved.</param>
    /// <param name="isExecutable">When true, exports are not generated (executables are not
    /// consumed by other packages).</param>
    /// <param name="srcRoot">TypeScript source root relative to the package root
    /// (e.g., <c>src</c>). When the output directory is a subdirectory of the source root
    /// (e.g., <c>src/domain</c>), the prefix (<c>domain</c>) is applied to dist paths and
    /// export subpaths so they mirror the build tool's directory structure.
    /// Default: inferred as the first path segment of the output directory relative to the
    /// package root.</param>
    /// <param name="importAlias">Optional subpath-imports alias (see
    /// <see cref="PathNaming.NormalizeImportAliasPrefix"/>). Only a configured alias writes
    /// <c>imports</c> entries (spec 004 FR-014); generated code never needs them.</param>
    /// <param name="subpathRoot">The package's <c>[EmitPackage(SubpathRoot = …)]</c>: a
    /// namespace prefix stripped from the <c>exports</c> keys only (spec 008). On-disk
    /// targets and <c>imports</c> are unaffected. Default: none.</param>
    /// <param name="runtimePeerSpecifier">Version specifier for the <c>metano-runtime</c> peer
    /// dependency (see <see cref="RuntimePeerDependency"/>); <c>null</c> when no emitted file
    /// imports the runtime, in which case nothing is added.</param>
    /// <param name="sourceCondition">Name of the export condition that resolves to the
    /// <c>.ts</c> sources (default <c>source</c>, see <see cref="SourceCondition"/>). A
    /// previously written source condition under another name is replaced, see
    /// <see cref="DropReplacedDefaultSourceCondition"/>.</param>
    /// <param name="packageVersion">When non-null, written to <c>package.json#version</c>,
    /// replacing a hand-written value: the same source as the range a cross-package
    /// consumer writes for this package, so the two cannot diverge (#765).</param>
    /// <param name="declaresDisposalHelpers">When true, declares the optional <c>tslib</c>
    /// peer dependency a downlevelled <c>using</c> imports its helpers from (see
    /// <see cref="DisposalHelpersPeerDependency"/>).</param>
    /// <returns>List of diagnostics raised while writing — empty in the happy path.</returns>
    public static IReadOnlyList<MetanoDiagnostic> UpdateOrCreate(
        string packageRoot,
        string outputDirAbsolute,
        IReadOnlyList<TsSourceFile> files,
        string distDirRelativeToPackageRoot = "./dist",
        string? authoritativePackageName = null,
        IReadOnlyDictionary<string, string>? crossPackageDependencies = null,
        bool isExecutable = false,
        string? srcRoot = null,
        string? importAlias = null,
        PackageSubpathRoot subpathRoot = default,
        string? runtimePeerSpecifier = null,
        SourceCondition sourceCondition = default,
        string? packageVersion = null,
        bool declaresDisposalHelpers = false
    )
    {
        var diagnostics = new List<MetanoDiagnostic>();
        var packageJsonPath = Path.Combine(packageRoot, PackageJsonFileName);
        var srcRelative = NormalizePath(Path.GetRelativePath(packageRoot, outputDirAbsolute));

        var (outputPrefix, prefixDiagnostic) = OutputPrefix.Resolve(srcRoot, srcRelative);

        if (prefixDiagnostic is not null)
            diagnostics.Add(prefixDiagnostic);

        // Build the imports/exports objects. Exports are only needed for libraries.
        var exports = isExecutable
            ? null
            : BuildExports(
                files,
                srcRelative,
                distDirRelativeToPackageRoot,
                outputPrefix,
                subpathRoot,
                sourceCondition
            );

        // Check for a root barrel from the file list: a configured alias also maps its
        // bare key to it. When outputPrefix is set, the root barrel is at {prefix}/index.ts.
        var rootIndexName = outputPrefix.Length > 0 ? $"{outputPrefix}/index.ts" : "index.ts";

        var hasRootIndex = files.Any(f =>
            NormalizePath(f.FileName).Equals(rootIndexName, StringComparison.Ordinal)
            || NormalizePath(f.FileName).Equals("index.ts", StringComparison.Ordinal)
        );

        var imports = BuildImports(
            srcRelative,
            distDirRelativeToPackageRoot,
            hasRootIndex,
            outputPrefix,
            importAlias,
            sourceCondition
        );

        JsonObject root;

        if (File.Exists(packageJsonPath))
        {
            var existing = File.ReadAllText(packageJsonPath);
            root = (JsonNode.Parse(existing) as JsonObject) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject
            {
                ["name"] = authoritativePackageName
                    ?? Path.GetFileName(packageRoot.TrimEnd('/', '\\')),
                ["private"] = true,
            };
        }

        ApplyAuthoritativePackageName(root, authoritativePackageName, diagnostics);
        ApplyPackageVersion(root, packageVersion, diagnostics);

        // Seed defaults when the consumer has not picked their own
        // values. The transpiler must not flip a hand-curated `type`
        // or `sideEffects` value back on every regeneration.
        WriteIfMissing(root, "type", "module");

        // Imports exist only for a configured alias (spec 004 FR-014): generated
        // code imports its files relatively, so no key is seeded without one, and
        // entries an earlier version wrote (`#`, `#/*`) are pruned only while they
        // still hold the writer's own mapping (see PruneUnaliasedImports). Aliased entries
        // follow the same write-if-missing rule as the scalar fields above: a
        // consumer who has retargeted an alias by hand (e.g. pointing `#app/*` at
        // `./lib/*` for a non-default build directory) keeps that mapping across
        // regenerations. Keys absent from the existing object are added; an
        // existing key is only touched when it still holds the transpiler's own
        // mapping, which then gains any condition introduced since (e.g. `source`).
        if (imports is not null)
            SeedOrUpgradeImports(root, imports, sourceCondition);
        else
            PruneUnaliasedImports(
                root,
                srcRelative,
                distDirRelativeToPackageRoot,
                outputPrefix,
                sourceCondition
            );

        // After the imports: a consumer who retargeted the compiled tree
        // (`#/*` → `./lib/*`) has the side-effect modules listed there too.
        var distBase = DistBase(distDirRelativeToPackageRoot, outputPrefix);
        var compiledBase = CompiledModuleBase(root, importAlias) ?? distBase;

        SeedOrUpgradeSideEffects(
            root,
            BuildSideEffectModules(files, srcRelative, compiledBase, distBase)
        );

        // Exports merge additively — see `MergeTranspilerManagedExports`
        // for the shape-detection rule and the deep-merge contract. Run
        // even when `exports` is empty (library that no longer emits
        // any barrel) so stale transpiler-shaped entries from a prior
        // run still get pruned while user-added subpaths survive.
        if (exports is not null)
        {
            MergeTranspilerManagedExports(
                root,
                exports,
                distDirRelativeToPackageRoot,
                sourceCondition
            );
        }

        // Merge auto-generated cross-package dependencies into the existing
        // `dependencies` object, leaving any user-hand-written entries for OTHER
        // packages alone. New entries are appended in ordinal order so the bytes never
        // depend on the order the transform discovered them in.
        if (crossPackageDependencies is { Count: > 0 })
        {
            var deps = root["dependencies"] as JsonObject ?? new JsonObject();

            foreach (
                var (pkg, version) in crossPackageDependencies.OrderBy(
                    entry => entry.Key,
                    StringComparer.Ordinal
                )
            )
                deps[pkg] = version;

            root["dependencies"] = deps;
        }

        if (runtimePeerSpecifier is not null)
            DeclareRuntimePeerDependency(root, runtimePeerSpecifier);

        if (declaresDisposalHelpers)
            DeclareDisposalHelpersPeerDependency(root);
        else
            RemoveOwnDisposalHelpersPeerDependency(root);

        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(packageJsonPath, root.ToJsonString(WriteOptions) + "\n");
        return diagnostics;
    }

    /// <summary>
    /// Writes <paramref name="version"/> to <c>package.json#version</c>: Metano owns the
    /// field, because a consumer's dependency range derives from the same source and a
    /// hand-written value could contradict it. Replacing a different value is reported as an
    /// informational diagnostic. A new field lands right after <c>name</c>.
    /// </summary>
    private static void ApplyPackageVersion(
        JsonObject root,
        string? version,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (version is null)
            return;

        var nameIndex = root.IndexOf("name");

        if (!root.ContainsKey("version") && nameIndex >= 0)
        {
            root.Insert(nameIndex + 1, "version", version);

            return;
        }

        if (StringOf(root["version"]) is { } existing && existing != version)
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Info,
                    DiagnosticCodes.CrossPackageResolution,
                    $"package.json#version '{existing}' was replaced by '{version}': Metano "
                        + "writes the version from the C# project (MSBuild Version, or "
                        + "[EmitPackage(Version)]), the same source as the range consumers get."
                )
            );

        root["version"] = version;
    }

    /// <summary>
    /// Without an alias no <c>imports</c> are written, and the bare <c>#</c> and <c>#/*</c>
    /// keys an earlier version seeded are rejected by Node 24 and Bun. An entry that still
    /// holds exactly the mapping this writer would have produced (shape-based ownership,
    /// as in <see cref="HoldsTranspilerMapping"/>) and carries no condition beyond the
    /// writer's own is removed; a retargeted, extended or host-authored one stays. An <c>imports</c> object left empty is removed too.
    /// </summary>
    private static void PruneUnaliasedImports(
        JsonObject root,
        string srcRelative,
        string distRelative,
        string outputPrefix,
        SourceCondition sourceCondition
    )
    {
        if (root["imports"] is not JsonObject existing)
            return;

        var owned = BuildImportEntries(
            UnaliasedPrefix,
            srcRelative,
            distRelative,
            hasRootIndex: true,
            outputPrefix,
            sourceCondition
        );

        foreach (var (key, generated) in owned)
        {
            if (
                existing[key] is JsonObject entry
                && generated is JsonObject generatedEntry
                && HoldsOnlyTranspilerMapping(entry, generatedEntry, sourceCondition)
            )
                existing.Remove(key);
        }

        if (existing.Count == 0)
            root.Remove("imports");
    }

    /// <summary>
    /// Declares <c>metano-runtime</c> in <c>peerDependencies</c>. The consumer's declaration
    /// wins: an entry in any other dependency section, or a peer entry whose value is not the
    /// shape this writer produces (e.g. <c>workspace:*</c>, <c>~2.7</c>), is left exactly as
    /// written. A peer entry that does have the writer's own shape is refreshed, so an upgrade
    /// of the compiler does not leave a stale range behind (shape-based ownership, as in
    /// <see cref="MergeTranspilerManagedExports"/>). A package that is itself
    /// <c>metano-runtime</c> never depends on itself.
    /// </summary>
    private static void DeclareRuntimePeerDependency(JsonObject root, string versionSpecifier)
    {
        if (ReadStringField(root, "name") == RuntimePeerDependency.PackageName)
            return;

        var declaredElsewhere = DependencySections
            .Where(section => section != PeerDependenciesSection)
            .Any(section => DeclaresRuntime(root, section));

        if (declaredElsewhere)
            return;

        var peers = root[PeerDependenciesSection] as JsonObject ?? new JsonObject();
        var existing = ReadStringField(peers, RuntimePeerDependency.PackageName);

        var isConsumerEntry =
            peers.ContainsKey(RuntimePeerDependency.PackageName)
            && !RuntimePeerDependency.IsOwnSpecifier(existing);

        if (isConsumerEntry)
            return;

        peers[RuntimePeerDependency.PackageName] = versionSpecifier;
        root[PeerDependenciesSection] = peers;
    }

    /// <summary>
    /// Declares <c>tslib</c> as an optional peer dependency. Any declaration of
    /// <c>tslib</c> the consumer already made, in any dependency section, wins and is left
    /// as written.
    /// </summary>
    private static void DeclareDisposalHelpersPeerDependency(JsonObject root)
    {
        var declared = DependencySections.Any(section =>
            root[section] is JsonObject entries
            && entries.ContainsKey(DisposalHelpersPeerDependency.PackageName)
        );

        if (declared)
            return;

        var peers = root[PeerDependenciesSection] as JsonObject ?? new JsonObject();
        peers[DisposalHelpersPeerDependency.PackageName] = DisposalHelpersPeerDependency.Specifier;
        root[PeerDependenciesSection] = peers;

        var meta = root[PeerDependenciesMetaSection] as JsonObject ?? new JsonObject();
        meta[DisposalHelpersPeerDependency.PackageName] = new JsonObject { ["optional"] = true };
        root[PeerDependenciesMetaSection] = meta;
    }

    /// <summary>
    /// Removes the optional <c>tslib</c> peer this writer declared once the package no longer
    /// declares a <c>using</c>. Only the writer's own entry goes: the
    /// <see cref="DisposalHelpersPeerDependency.Specifier"/> range marked optional in
    /// <c>peerDependenciesMeta</c> (<see cref="DisposalHelpersPeerDependency.IsOwnEntry"/>);
    /// any other <c>tslib</c> entry is the consumer's. A section left empty is removed. A
    /// hand-written optional <c>tslib: "^2.6.0"</c> peer cannot be told apart from the
    /// writer's own entry, so it is removed with it.
    /// </summary>
    private static void RemoveOwnDisposalHelpersPeerDependency(JsonObject root)
    {
        var peers = root[PeerDependenciesSection] as JsonObject;
        var meta = root[PeerDependenciesMetaSection] as JsonObject;
        var name = DisposalHelpersPeerDependency.PackageName;

        var isOwnEntry = DisposalHelpersPeerDependency.IsOwnEntry(
            peers is null ? null : ReadStringField(peers, name),
            IsMarkedOptional(meta, name)
        );

        if (!isOwnEntry)
            return;

        RemoveEntry(root, PeerDependenciesSection, name);
        RemoveEntry(root, PeerDependenciesMetaSection, name);
    }

    private static bool IsMarkedOptional(JsonObject? meta, string name) =>
        meta?[name]?["optional"] is JsonValue optional
        && optional.TryGetValue<bool>(out var isOptional)
        && isOptional;

    private static void RemoveEntry(JsonObject root, string section, string name)
    {
        if (root[section] is not JsonObject entries)
            return;

        entries.Remove(name);

        if (entries.Count == 0)
            root.Remove(section);
    }

    private static bool DeclaresRuntime(JsonObject root, string section) =>
        root[section] is JsonObject declared
        && declared.ContainsKey(RuntimePeerDependency.PackageName);

    /// <summary>
    /// Seeds <c>package.json#name</c> from <c>[assembly: EmitPackage(...)]</c>
    /// on first creation. When the file already declares a name the
    /// existing value wins; on divergence we surface MS0007 so the
    /// consumer can re-align the two sources, but we never silently
    /// overwrite a hand-edited name (a published package may have
    /// links / docs / consumers tied to it).
    /// </summary>
    private static void ApplyAuthoritativePackageName(
        JsonObject root,
        string? authoritativePackageName,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (authoritativePackageName is null)
            return;

        var existingName = root["name"]?.GetValue<string>();

        if (existingName is null)
        {
            root["name"] = authoritativePackageName;
            return;
        }

        if (existingName == authoritativePackageName)
            return;

        diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.CrossPackageResolution,
                $"package.json#name '{existingName}' diverges from "
                    + $"[assembly: EmitPackage(\"{authoritativePackageName}\")]. "
                    + $"The existing name was preserved; consumers keep importing via "
                    + $"'{existingName}'. Update either the attribute or package.json#name "
                    + $"to align them."
            )
        );
    }

    /// <summary>
    /// The package-relative paths of the modules that run code when merely imported (a
    /// <c>[ModuleEntryPoint]</c> module, a strict-union-guard variant): both the compiled
    /// <c>.js</c> under <paramref name="compiledBase"/> and the <c>.ts</c> source, since a
    /// bundler matches <c>sideEffects</c> against whichever the resolution picked (#368).
    /// Entries under <paramref name="distBase"/>, the writer's default compiled tree, are
    /// its own too, so a retargeted package drops what an earlier run listed there.
    /// </summary>
    private static SideEffectModules BuildSideEffectModules(
        IReadOnlyList<TsSourceFile> files,
        string srcRelative,
        string compiledBase,
        string distBase
    )
    {
        var src = NormalizePath(srcRelative).TrimEnd('/');
        var targets = new List<string>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var fileName = NormalizePath(file.FileName);
            var withoutExt = fileName[..fileName.LastIndexOf('.')];
            var paths = new[] { $"./{compiledBase}/{withoutExt}.js", SourceTarget(src, fileName) };

            emitted.UnionWith(paths);

            if (file.HasModuleSideEffects)
                targets.AddRange(paths);
        }

        targets.Sort(StringComparer.Ordinal);

        string[] ownedTrees =
        [
            .. new[] { compiledBase, distBase, src }
                .Where(tree => tree.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Select(tree => $"./{tree}/"),
        ];

        return new SideEffectModules(targets, emitted, ownedTrees);
    }

    /// <summary>The compiled tree the writer maps its import alias to: the dist directory, under the output prefix.</summary>
    private static string DistBase(string distRelative, string outputPrefix)
    {
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var prefix = outputPrefix.TrimEnd('/');

        return prefix.Length > 0 ? $"{dist}/{prefix}" : dist;
    }

    /// <summary>
    /// The compiled tree the package's own <c>#&lt;alias&gt;/*</c> import (or the <c>#/*</c>
    /// an earlier version seeded, when no alias is set) resolves to: its <c>import</c>
    /// condition, or the plain string target, without the
    /// trailing <c>/*</c> or <c>/*.js</c> (<c>"./lib/*"</c> is <c>lib</c>). <c>null</c>
    /// when the entry has no such target, so the writer's dist directory applies.
    /// </summary>
    private static string? CompiledModuleBase(JsonObject root, string? importAlias)
    {
        var starKey = $"{PathNaming.NormalizeImportAliasPrefix(importAlias)}/*";

        var target = root["imports"]?[starKey] switch
        {
            JsonValue plain => StringOf(plain),
            JsonObject conditions => StringOf(conditions["import"]),
            _ => null,
        };

        var stripped = CompiledTreeSuffixes
            .Where(suffix => target?.EndsWith(suffix, StringComparison.Ordinal) == true)
            .Select(suffix => target![..^suffix.Length])
            .FirstOrDefault();

        return stripped is null ? null : NormalizePath(stripped);
    }

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Seeds <c>sideEffects</c> as the list of side-effect modules (or <c>false</c> when
    /// there are none). A flat <c>false</c> is the writer's own earlier default, so it is
    /// upgraded to the list; a list keeps the entries the consumer added by hand and
    /// replaces the ones this writer owns; any other value (<c>true</c>, a hand-written
    /// string) is the consumer's and stays.
    /// </summary>
    private static void SeedOrUpgradeSideEffects(JsonObject root, SideEffectModules modules)
    {
        var existing = root["sideEffects"];
        var previous = existing as JsonArray;

        var isFlatFalse =
            existing is JsonValue flag && flag.TryGetValue<bool>(out var value) && !value;

        if (existing is not null && previous is null && !isFlatFalse)
            return;

        // An entry that is not a string is the consumer's (bundlers ignore it); it
        // stays as written instead of failing the run.
        var kept = (previous ?? [])
            .Where(entry => StringOf(entry) is not { } path || !modules.Owns(path))
            .Select(entry => entry?.DeepClone());

        var merged = kept.Concat(modules.Targets.Select(path => (JsonNode?)JsonValue.Create(path)))
            .ToList();

        root["sideEffects"] =
            merged.Count == 0 ? JsonValue.Create(false) : new JsonArray([.. merged]);
    }

    /// <summary>
    /// The side-effect paths of a run: the <paramref name="Targets"/> to list, and the
    /// paths a previous run may have written (every emitted file, or anything under the
    /// dist or source tree) so a stale entry is replaced while a hand-added one is kept.
    /// </summary>
    private sealed record SideEffectModules(
        IReadOnlyList<string> Targets,
        IReadOnlySet<string> Emitted,
        IReadOnlyList<string> OwnedTrees
    )
    {
        public bool Owns(string entry) =>
            Emitted.Contains(entry)
            || OwnedTrees.Any(tree => entry.StartsWith(tree, StringComparison.Ordinal));
    }

    /// <summary>Seeds <paramref name="key"/> only when missing.</summary>
    /// <remarks>
    /// Used for scalar defaults like <c>type</c> and <c>sideEffects</c>:
    /// once the consumer has set a value by hand, the transpiler never
    /// overwrites it on regeneration.
    /// </remarks>
    private static void WriteIfMissing(JsonObject root, string key, JsonNode? value)
    {
        if (root.ContainsKey(key))
            return;

        root[key] = value;
    }

    /// <inheritdoc cref="WriteIfMissing(JsonObject, string, JsonNode?)"/>
    private static void WriteIfMissing(JsonObject root, string key, bool value)
    {
        if (root.ContainsKey(key))
            return;

        root[key] = value;
    }

    /// <summary>
    /// Merges the transpiler-emitted exports into the existing
    /// <c>exports</c> object. Two rules combine to keep user
    /// customizations intact across regenerations:
    /// <list type="number">
    ///   <item><b>Stale removal.</b> An existing entry whose key is
    ///   not in <paramref name="generated"/> is dropped only when
    ///   its value passes <see cref="IsTranspilerEmittedExportEntry"/>
    ///   (looks like a previous transpiler output). User-added
    ///   subpaths pointing outside the dist tree (assets, shims)
    ///   survive.</item>
    ///   <item><b>Per-key deep-merge.</b> When a generated key is
    ///   already present, the transpiler's <c>types</c>/<c>import</c>
    ///   fields refresh in place and any extra conditional fields
    ///   the consumer added (e.g. <c>require</c>, <c>default</c>)
    ///   stay untouched.</item>
    /// </list>
    /// </summary>
    private static void MergeTranspilerManagedExports(
        JsonObject root,
        JsonObject generated,
        string distRelative,
        SourceCondition sourceCondition
    )
    {
        // Trailing slash is mandatory: with a bare `./dist` prefix,
        // `StartsWith` would also match sibling directories like
        // `./dist2/...` or `./dist-cjs/...` and misclassify the
        // consumer's hand-curated exports there as stale transpiler
        // output. The slash anchors the comparison to the directory
        // boundary.
        var transpilerDistPrefix = "./" + NormalizePath(distRelative).TrimEnd('/') + "/";

        if (root["exports"] is not JsonObject existing)
        {
            root["exports"] = generated;
            return;
        }

        DropStaleTranspilerEntries(existing, generated, transpilerDistPrefix);
        MergeGeneratedEntries(existing, generated, sourceCondition);
    }

    private static void DropStaleTranspilerEntries(
        JsonObject existing,
        JsonObject generated,
        string transpilerDistPrefix
    )
    {
        foreach (var (entryKey, entryValue) in existing.ToList())
        {
            if (generated.ContainsKey(entryKey))
                continue;

            if (IsTranspilerEmittedExportEntry(entryValue, transpilerDistPrefix))
                existing.Remove(entryKey);
        }
    }

    private static void MergeGeneratedEntries(
        JsonObject existing,
        JsonObject generated,
        SourceCondition sourceCondition
    )
    {
        foreach (var (entryKey, entryValue) in generated)
        {
            if (
                existing[entryKey] is JsonObject existingEntry
                && entryValue is JsonObject generatedEntry
            )
            {
                DropReplacedDefaultSourceCondition(existingEntry, generatedEntry, sourceCondition);
                MergeConditions(existingEntry, generatedEntry);
            }
            else
            {
                existing[entryKey] = entryValue?.DeepClone();
            }
        }
    }

    /// <summary>
    /// On a rename away from the default, removes the <c>source</c> condition the previous
    /// run wrote, so the old name does not keep resolving to the sources. It acts only on
    /// an actual rename: nothing is removed when the entry already carries the configured
    /// condition (the steady state, identical to builds before the name was configurable),
    /// nor when the configured name is the default itself. Ownership rule: the removed
    /// condition is exactly <c>source</c>, and only when it points at the very <c>.ts</c>
    /// target the writer emits now, so a retargeted <c>source</c> and every other
    /// condition (<c>development</c>, <c>require</c>, ...) are never touched. The writer
    /// persists no earlier name, so after a custom-to-custom rename, or a return to the
    /// default, the previous custom key stays for the consumer to delete.
    /// </summary>
    private static void DropReplacedDefaultSourceCondition(
        JsonObject existingEntry,
        JsonObject generatedEntry,
        SourceCondition sourceCondition
    )
    {
        if (sourceCondition.IsDefault || existingEntry.ContainsKey(sourceCondition.Name))
            return;

        if (!existingEntry.TryGetPropertyValue(SourceCondition.DefaultName, out var previous))
            return;

        if (JsonNode.DeepEquals(previous, generatedEntry[sourceCondition.Name]))
            existingEntry.Remove(SourceCondition.DefaultName);
    }

    /// <summary>
    /// Copies every generated condition into <paramref name="existingEntry"/>
    /// while keeping the resolution order meaningful. Node and bundlers
    /// pick the first matching key of a conditional entry, so a condition
    /// is only useful at the right position: a condition already present
    /// is refreshed in place, and a missing one is inserted right after
    /// the previous generated condition (or at the top when it is the
    /// first one, as <c>source</c> is). Conditions the consumer added keep
    /// their relative position.
    /// </summary>
    private static void MergeConditions(JsonObject existingEntry, JsonObject generatedEntry)
    {
        var insertionIndex = 0;

        foreach (var (condition, target) in generatedEntry)
        {
            var existingIndex = existingEntry.IndexOf(condition);

            if (existingIndex >= 0)
            {
                existingEntry[condition] = target?.DeepClone();
                // Never move the cursor backwards: when the consumer reordered
                // the generated conditions, a later one (e.g. `default`) must
                // still land after every generated condition seen so far.
                insertionIndex = Math.Max(insertionIndex, existingIndex + 1);
                continue;
            }

            existingEntry.Insert(insertionIndex, condition, target?.DeepClone());
            insertionIndex++;
        }
    }

    /// <summary>
    /// True when an exports-entry value has the structural shape the
    /// transpiler emits: a JSON object whose <c>types</c> and
    /// <c>import</c> strings both point into the package's dist
    /// directory. Used to tell stale transpiler outputs apart from
    /// user-added entries that happen to live alongside them.
    /// </summary>
    private static bool IsTranspilerEmittedExportEntry(JsonNode? value, string transpilerDistPrefix)
    {
        if (value is not JsonObject obj)
            return false;

        var types = ReadStringField(obj, "types");
        var import = ReadStringField(obj, "import");

        if (types is null || import is null)
            return false;

        return types.StartsWith(transpilerDistPrefix, StringComparison.Ordinal)
            && import.StartsWith(transpilerDistPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a string-valued field from an exports-entry object,
    /// returning null when the field is absent or holds a non-string
    /// node (object, array, number). Defensive read: a hand-edited
    /// <c>package.json</c> may put any JSON shape under
    /// <c>types</c>/<c>import</c>, and <see cref="JsonValue.GetValue{T}"/>
    /// throws on a type mismatch — crashing the transpiler on
    /// merely-unusual user input is not acceptable.
    /// </summary>
    private static string? ReadStringField(JsonObject obj, string fieldName)
    {
        if (obj[fieldName] is not JsonValue value)
            return null;

        return value.TryGetValue<string>(out var s) ? s : null;
    }

    /// <summary>
    /// Seeds the <c>imports</c> object with the configured alias's
    /// <c>#&lt;alias&gt;</c> / <c>#&lt;alias&gt;/*</c> entries on first creation. On
    /// subsequent
    /// runs a key the consumer retargeted by hand (for a custom build
    /// dir, alternate dist layout, or wrapper pre-processor) keeps that
    /// mapping — the transpiler only fills in keys the consumer has not
    /// picked yet, and upgrades keys that still hold its own mapping so
    /// manifests written by an older version gain new conditions such
    /// as <c>source</c> and <c>bun</c>.
    /// </summary>
    private static void SeedOrUpgradeImports(
        JsonObject root,
        JsonObject generated,
        SourceCondition sourceCondition
    )
    {
        if (root["imports"] is not JsonObject existing)
        {
            root["imports"] = generated;
            return;
        }

        foreach (var (entryKey, entryValue) in generated)
        {
            if (!existing.ContainsKey(entryKey))
            {
                existing[entryKey] = entryValue?.DeepClone();
                continue;
            }

            UpgradeTranspilerMapping(existing[entryKey], entryValue, sourceCondition);
        }
    }

    /// <summary>
    /// Merges the generated conditions into an existing imports entry
    /// only while it still holds the transpiler's own mapping; a
    /// retargeted or non-object entry is left exactly as the consumer
    /// wrote it.
    /// </summary>
    private static void UpgradeTranspilerMapping(
        JsonNode? existingValue,
        JsonNode? generatedValue,
        SourceCondition sourceCondition
    )
    {
        if (existingValue is not JsonObject existingEntry)
            return;

        if (generatedValue is not JsonObject generatedEntry)
            return;

        if (!HoldsTranspilerMapping(existingEntry, generatedEntry, sourceCondition))
            return;

        DropReplacedDefaultSourceCondition(existingEntry, generatedEntry, sourceCondition);
        MergeConditions(existingEntry, generatedEntry);
    }

    /// <summary>
    /// True when <paramref name="existingEntry"/> still carries the full
    /// mapping an earlier transpiler seeded: every generated condition
    /// except the later ones (the renamable source condition and <c>bun</c>) is present
    /// and points where the transpiler would point it (a later condition is compared
    /// once the entry declares it). A host-authored alias that
    /// merely overlaps (e.g. only <c>default</c>), a retargeted one, or one
    /// the consumer trimmed is left untouched. Extra consumer conditions
    /// do not count as divergence.
    /// </summary>
    private static bool HoldsTranspilerMapping(
        JsonObject existingEntry,
        JsonObject generatedEntry,
        SourceCondition sourceCondition
    )
    {
        // The source and bun conditions count only once present, so a consumer who
        // retargeted one keeps that mapping, and a manifest written under another
        // condition name (or by an older version) still holds the mapping.
        var seededConditions = generatedEntry.Where(condition =>
            !IsLaterCondition(condition.Key, sourceCondition)
            || existingEntry.ContainsKey(condition.Key)
        );

        return seededConditions.All(condition =>
            existingEntry.TryGetPropertyValue(condition.Key, out var target)
            && JsonNode.DeepEquals(target, condition.Value)
        );
    }

    /// <summary>
    /// <see cref="HoldsTranspilerMapping"/> and no condition of the consumer's own: a
    /// <c>source</c> condition under the default name counts as the writer's, since an earlier
    /// run wrote it.
    /// </summary>
    private static bool HoldsOnlyTranspilerMapping(
        JsonObject existingEntry,
        JsonObject generatedEntry,
        SourceCondition sourceCondition
    ) =>
        HoldsTranspilerMapping(existingEntry, generatedEntry, sourceCondition)
        && existingEntry.All(condition =>
            generatedEntry.ContainsKey(condition.Key)
            || condition.Key == SourceCondition.DefaultName
        );

    /// <summary>
    /// The conditions added to the transpiler's <c>imports</c> entries after they were
    /// first seeded: the (renamable) source condition and <c>bun</c> (spec 004 FR-014).
    /// </summary>
    private static bool IsLaterCondition(string condition, SourceCondition sourceCondition) =>
        condition == sourceCondition.Name || condition == BunCondition;

    /// <summary>
    /// Builds the <c>imports</c> object of a configured alias (<c>#&lt;alias&gt;/*</c>, plus
    /// <c>#&lt;alias&gt;</c> when a root barrel exists); <c>null</c> without one, since
    /// generated code imports its files relatively (spec 004 FR-014). A bare <c>#</c> or
    /// <c>#/*</c> key is never written: Node 24, Bun and TypeScript reject the first, and
    /// Node 24 and Bun the second. Format: an opt-in <c>source</c> condition comes first
    /// (resolves straight to the .ts sources for tools configured with it, e.g. Vite
    /// <c>resolve.conditions</c> or TS <c>customConditions</c>), then <c>types</c>, then
    /// <c>bun</c> (the .ts sources, before <c>import</c>, so Bun never needs a prior
    /// build), then dist <c>import</c>, with the source .ts as the <c>default</c>.
    /// </summary>
    private static JsonObject? BuildImports(
        string srcRelative,
        string distRelative,
        bool hasRootIndex,
        string outputPrefix,
        string? importAlias,
        SourceCondition sourceCondition
    )
    {
        var aliasPrefix = PathNaming.NormalizeImportAliasPrefix(importAlias);

        return aliasPrefix == UnaliasedPrefix
            ? null
            : BuildImportEntries(
                aliasPrefix,
                srcRelative,
                distRelative,
                hasRootIndex,
                outputPrefix,
                sourceCondition
            );
    }

    private static JsonObject BuildImportEntries(
        string aliasPrefix,
        string srcRelative,
        string distRelative,
        bool hasRootIndex,
        string outputPrefix,
        SourceCondition sourceCondition
    )
    {
        var src = NormalizePath(srcRelative).TrimEnd('/');
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var prefix = outputPrefix.TrimEnd('/');
        var distBase = prefix.Length > 0 ? $"{dist}/{prefix}" : dist;
        var starKey = $"{aliasPrefix}/*";

        var imports = new JsonObject
        {
            [starKey] = new JsonObject
            {
                [sourceCondition.Name] = SourceTarget(src, "*.ts"),
                ["types"] = $"./{distBase}/*.d.ts",
                [BunCondition] = SourceTarget(src, "*.ts"),
                ["import"] = $"./{distBase}/*.js",
                ["default"] = SourceTarget(src, "*.ts"),
            },
        };

        if (hasRootIndex)
        {
            imports[aliasPrefix] = new JsonObject
            {
                [sourceCondition.Name] = SourceTarget(src, "index.ts"),
                ["types"] = $"./{distBase}/index.d.ts",
                [BunCondition] = SourceTarget(src, "index.ts"),
                ["import"] = $"./{distBase}/index.js",
                ["default"] = SourceTarget(src, "index.ts"),
            };
        }

        return imports;
    }

    /// <summary>
    /// Builds the <c>exports</c> object listing the public subpaths the consumer can import.
    /// Only namespace barrel files (<c>index.ts</c>) become export entries — individual type
    /// files are accessed through their barrel, matching the namespace-first convention
    /// from ADR-0006. The key is the barrel's exported directory (its directory with the
    /// declared <paramref name="subpathRoot"/> stripped, see
    /// <see cref="PackageExportSubpaths"/>), prefixed with <paramref name="outputPrefix"/>
    /// when non-empty; the entry targets always point at the real on-disk files. Key
    /// collisions are reported by the transform (MS0030); here the barrel that
    /// <see cref="PackageExportSubpaths.Resolve"/> keeps simply wins. Each entry lists the
    /// opt-in <c>source</c> condition first (the barrel's .ts file), so tools configured
    /// with it read the sources directly while default resolution keeps using dist.
    /// </summary>
    private static JsonObject BuildExports(
        IReadOnlyList<TsSourceFile> files,
        string srcRelative,
        string distRelative,
        string outputPrefix,
        PackageSubpathRoot subpathRoot,
        SourceCondition sourceCondition
    )
    {
        var src = NormalizePath(srcRelative).TrimEnd('/');
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var prefix = outputPrefix.TrimEnd('/');
        var distBase = prefix.Length > 0 ? $"{dist}/{prefix}" : dist;
        var exports = new JsonObject();

        var resolution = PackageExportSubpaths.Resolve(files.Select(f => f.FileName), subpathRoot);

        foreach (var barrel in resolution.Barrels)
        {
            var withoutExt = barrel.FileName[..^3]; // Strip .ts

            exports[ExportKey(prefix, barrel.ExportedDirectory)] = new JsonObject
            {
                [sourceCondition.Name] = SourceTarget(src, $"{withoutExt}.ts"),
                ["types"] = $"./{distBase}/{withoutExt}.d.ts",
                ["import"] = $"./{distBase}/{withoutExt}.js",
            };
        }

        return exports;
    }

    /// <summary>
    /// Formats an exports key: <c>"" → "."</c>, <c>"users" → "./users"</c>, and with
    /// an output prefix <c>"" → "./domain"</c>, <c>"users" → "./domain/users"</c>.
    /// </summary>
    private static string ExportKey(string prefix, string exportedDirectory)
    {
        var path = (prefix, exportedDirectory) switch
        {
            ("", var directory) => directory,
            (var outputRoot, "") => outputRoot,
            var (outputRoot, directory) => $"{outputRoot}/{directory}",
        };

        return path.Length == 0 ? "." : $"./{path}";
    }

    /// <summary>
    /// Package-relative target for a generated .ts file. The source dir is
    /// empty when the output dir is the package root; joining it naively
    /// would yield <c>.//index.ts</c>, an empty segment Node rejects.
    /// </summary>
    private static string SourceTarget(string src, string relativePath) =>
        src.Length > 0 ? $"./{src}/{relativePath}" : $"./{relativePath}";

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('.', '/');
}
