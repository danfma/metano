using System.Text.Json;
using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano;

/// <summary>
/// Generates and updates the consumer's package.json with <c>imports</c>, <c>exports</c>,
/// <c>sideEffects</c>, and <c>type</c> fields based on the TypeScript files emitted by the
/// transpiler.
///
/// Strategy: non-destructive merge. The user's hand-written fields (name, dependencies,
/// scripts, etc.) are preserved. Transpiler-managed entries inside <c>imports</c> and
/// <c>exports</c> are merged into the existing objects — user-defined entries for other
/// subpaths survive untouched.
/// </summary>
public static class PackageJsonWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Conditions the transpiler started emitting after its first <c>imports</c>
    /// seeding. A manifest written by an older version lacks them, so they are
    /// ignored when deciding whether an existing entry still holds the
    /// transpiler's own mapping.
    /// </summary>
    private static readonly HashSet<string> ConditionsIntroducedAfterSeeding = ["source"];

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
    /// <see cref="PathNaming.NormalizeImportAliasPrefix"/>).</param>
    /// <param name="subpathRoot">The package's <c>[EmitPackage(SubpathRoot = …)]</c>: a
    /// namespace prefix stripped from the <c>exports</c> keys only (spec 008). On-disk
    /// targets and <c>imports</c> are unaffected. Default: none.</param>
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
        PackageSubpathRoot subpathRoot = default
    )
    {
        var diagnostics = new List<MetanoDiagnostic>();
        var packageJsonPath = Path.Combine(packageRoot, "package.json");
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
                subpathRoot
            );

        // Check for a root barrel from the file list — executables skip exports
        // but may still need the "#" import alias for internal barrel imports.
        // When outputPrefix is set, the root barrel is at {prefix}/index.ts.
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
            importAlias
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
                ["name"] =
                    authoritativePackageName ?? Path.GetFileName(packageRoot.TrimEnd('/', '\\')),
                ["private"] = true,
            };
        }

        ApplyAuthoritativePackageName(root, authoritativePackageName, diagnostics);

        // Seed defaults when the consumer has not picked their own
        // values. The transpiler must not flip a hand-curated `type`
        // or `sideEffects` value back on every regeneration.
        WriteIfMissing(root, "type", "module");
        WriteIfMissing(root, "sideEffects", false);

        // Imports follow the same write-if-missing rule as the
        // scalar fields above: the transpiler seeds its alias keys
        // (`#` and `#/*` by default, or `#<alias>` and `#<alias>/*`
        // when --import-alias is set) on first creation, but a
        // consumer who has retargeted an alias by hand (e.g. pointing
        // `#/*` at `./lib/*` for a non-default build directory) keeps
        // that mapping across regenerations. Keys absent from the
        // existing object are added; an existing key is only touched
        // when it still holds the transpiler's own mapping, which then
        // gains any condition introduced since (e.g. `source`).
        SeedOrUpgradeImports(root, imports);

        // Exports merge additively — see `MergeTranspilerManagedExports`
        // for the shape-detection rule and the deep-merge contract. Run
        // even when `exports` is empty (library that no longer emits
        // any barrel) so stale transpiler-shaped entries from a prior
        // run still get pruned while user-added subpaths survive.
        if (exports is not null)
        {
            MergeTranspilerManagedExports(root, exports, distDirRelativeToPackageRoot);
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

        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(packageJsonPath, root.ToJsonString(WriteOptions) + "\n");
        return diagnostics;
    }

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
        string distRelative
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
        MergeGeneratedEntries(existing, generated);
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

    private static void MergeGeneratedEntries(JsonObject existing, JsonObject generated)
    {
        foreach (var (entryKey, entryValue) in generated)
        {
            if (
                existing[entryKey] is JsonObject existingEntry
                && entryValue is JsonObject generatedEntry
            )
            {
                MergeConditions(existingEntry, generatedEntry);
            }
            else
            {
                existing[entryKey] = entryValue?.DeepClone();
            }
        }
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
    /// Seeds the <c>imports</c> object with the transpiler's
    /// <c>#</c> / <c>#/*</c> aliases on first creation. On subsequent
    /// runs a key the consumer retargeted by hand (for a custom build
    /// dir, alternate dist layout, or wrapper pre-processor) keeps that
    /// mapping — the transpiler only fills in keys the consumer has not
    /// picked yet, and upgrades keys that still hold its own mapping so
    /// manifests written by an older version gain new conditions such
    /// as <c>source</c>.
    /// </summary>
    private static void SeedOrUpgradeImports(JsonObject root, JsonObject generated)
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

            UpgradeTranspilerMapping(existing[entryKey], entryValue);
        }
    }

    /// <summary>
    /// Merges the generated conditions into an existing imports entry
    /// only while it still holds the transpiler's own mapping; a
    /// retargeted or non-object entry is left exactly as the consumer
    /// wrote it.
    /// </summary>
    private static void UpgradeTranspilerMapping(JsonNode? existingValue, JsonNode? generatedValue)
    {
        if (existingValue is not JsonObject existingEntry)
            return;

        if (generatedValue is not JsonObject generatedEntry)
            return;

        if (HoldsTranspilerMapping(existingEntry, generatedEntry))
            MergeConditions(existingEntry, generatedEntry);
    }

    /// <summary>
    /// True when <paramref name="existingEntry"/> still carries the full
    /// mapping an earlier transpiler seeded: every generated condition
    /// except the ones introduced after seeding is present and points
    /// where the transpiler would point it (a late condition is compared
    /// once the entry declares it). A host-authored alias that
    /// merely overlaps (e.g. only <c>default</c>), a retargeted one, or one
    /// the consumer trimmed is left untouched. Extra consumer conditions
    /// do not count as divergence.
    /// </summary>
    private static bool HoldsTranspilerMapping(JsonObject existingEntry, JsonObject generatedEntry)
    {
        // A late condition counts only once present, so a consumer who
        // retargeted `source` keeps that mapping.
        var seededConditions = generatedEntry.Where(condition =>
            !ConditionsIntroducedAfterSeeding.Contains(condition.Key)
            || existingEntry.ContainsKey(condition.Key)
        );

        return seededConditions.All(condition =>
            existingEntry.TryGetPropertyValue(condition.Key, out var target)
            && JsonNode.DeepEquals(target, condition.Value)
        );
    }

    /// <summary>
    /// Builds the <c>imports</c> object with conditional exports for the <c>#/*</c> alias.
    /// Format: an opt-in <c>source</c> condition comes first (resolves straight to the
    /// .ts sources for tools configured with it, e.g. Vite <c>resolve.conditions</c> or
    /// TS <c>customConditions</c>), then dist (.js + .d.ts), with the source .ts as the
    /// <c>default</c> fallback for dev.
    /// </summary>
    private static JsonObject BuildImports(
        string srcRelative,
        string distRelative,
        bool hasRootIndex,
        string outputPrefix,
        string? importAlias
    )
    {
        var src = NormalizePath(srcRelative).TrimEnd('/');
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var prefix = outputPrefix.TrimEnd('/');
        var distBase = prefix.Length > 0 ? $"{dist}/{prefix}" : dist;

        // Default `#` → keys `#/*` and `#`; a configured alias → `#<alias>/*` and
        // `#<alias>`. When an alias is set the default `#`/`#/*` keys are never emitted,
        // so a host project's own `#` alias is left untouched.
        var aliasPrefix = PathNaming.NormalizeImportAliasPrefix(importAlias);
        var starKey = $"{aliasPrefix}/*";

        var imports = new JsonObject
        {
            [starKey] = new JsonObject
            {
                ["source"] = SourceTarget(src, "*.ts"),
                ["types"] = $"./{distBase}/*.d.ts",
                ["import"] = $"./{distBase}/*.js",
                ["default"] = SourceTarget(src, "*.ts"),
            },
        };

        if (hasRootIndex)
        {
            imports[aliasPrefix] = new JsonObject
            {
                ["source"] = SourceTarget(src, "index.ts"),
                ["types"] = $"./{distBase}/index.d.ts",
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
        PackageSubpathRoot subpathRoot
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
                ["source"] = SourceTarget(src, $"{withoutExt}.ts"),
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
