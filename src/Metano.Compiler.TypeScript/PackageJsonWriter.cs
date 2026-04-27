using System.Text.Json;
using System.Text.Json.Nodes;
using Metano.Compiler.Diagnostics;
using Metano.TypeScript.AST;

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
    /// <returns>List of diagnostics raised while writing — empty in the happy path.</returns>
    public static IReadOnlyList<MetanoDiagnostic> UpdateOrCreate(
        string packageRoot,
        string outputDirAbsolute,
        IReadOnlyList<TsSourceFile> files,
        string distDirRelativeToPackageRoot = "./dist",
        string? authoritativePackageName = null,
        IReadOnlyDictionary<string, string>? crossPackageDependencies = null,
        bool isExecutable = false,
        string? srcRoot = null
    )
    {
        var diagnostics = new List<MetanoDiagnostic>();
        var packageJsonPath = Path.Combine(packageRoot, "package.json");
        var srcRelative = NormalizePath(Path.GetRelativePath(packageRoot, outputDirAbsolute));

        // Resolve the source root: explicit parameter, or infer from first path segment.
        // "--src-root ." means the package root itself is the source root, so the full
        // srcRelative becomes the output prefix (e.g., "src/domain" → prefix "src/domain").
        var rawSrcRoot = srcRoot?.Replace('\\', '/').TrimStart('/').TrimEnd('/');
        string resolvedSrcRoot;
        if (rawSrcRoot is null or "")
            resolvedSrcRoot = srcRelative.Split('/')[0]; // infer from first segment
        else if (rawSrcRoot == ".")
            resolvedSrcRoot = ""; // package root is the source root
        else
            resolvedSrcRoot = rawSrcRoot.TrimStart('.', '/');

        // Output prefix: the path from the source root to the output directory.
        // Empty when outputDir IS the source root (e.g., srcRelative = "src").
        // Validate that srcRelative is within resolvedSrcRoot — if not, fall back
        // to empty prefix and warn rather than producing silently wrong paths.
        string outputPrefix;
        if (resolvedSrcRoot.Length == 0)
        {
            // Package root is the source root — full srcRelative is the prefix.
            outputPrefix = srcRelative;
        }
        else if (srcRelative == resolvedSrcRoot)
        {
            outputPrefix = "";
        }
        else if (srcRelative.StartsWith(resolvedSrcRoot + "/", StringComparison.Ordinal))
        {
            outputPrefix = srcRelative[(resolvedSrcRoot.Length + 1)..];
        }
        else
        {
            outputPrefix = "";
            diagnostics.Add(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.CrossPackageResolution,
                    $"--src-root '{resolvedSrcRoot}' is not an ancestor of output path "
                        + $"'{srcRelative}'. Ignoring prefix — imports/exports paths may be incorrect. "
                        + $"Set --src-root to the directory that your build tool uses as rootDir."
                )
            );
        }

        // Build the imports/exports objects. Exports are only needed for libraries.
        var exports = isExecutable
            ? null
            : BuildExports(files, distDirRelativeToPackageRoot, outputPrefix);
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
            outputPrefix
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

        // Imports: the transpiler owns `#` and `#/*` — stale aliases
        // are removed; user-defined keys survive.
        HashSet<string> managedImportKeys = ["#", "#/*"];
        ReplacePreservingUserEntries(root, "imports", imports, managedImportKeys);

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
        // packages alone.
        if (crossPackageDependencies is { Count: > 0 })
        {
            var deps = root["dependencies"] as JsonObject ?? new JsonObject();
            foreach (var (pkg, version) in crossPackageDependencies)
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
                foreach (var (fieldKey, fieldValue) in generatedEntry)
                    existingEntry[fieldKey] = fieldValue?.DeepClone();
            }
            else
            {
                existing[entryKey] = entryValue?.DeepClone();
            }
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
    /// Replaces the JSON object at <paramref name="key"/> with <paramref name="generated"/>,
    /// preserving any user-defined entries whose keys are absent from the generated set.
    /// Keys listed in <paramref name="managedKeys"/> are considered transpiler-managed — if
    /// they appear in the existing object but not in <paramref name="generated"/>, they are
    /// stale and silently dropped instead of being preserved.
    /// </summary>
    private static void ReplacePreservingUserEntries(
        JsonObject root,
        string key,
        JsonObject generated,
        IReadOnlySet<string>? managedKeys = null
    )
    {
        if (root[key] is JsonObject existing)
        {
            // Copy user-defined entries that the transpiler doesn't manage.
            foreach (var (entryKey, entryValue) in existing.ToList())
            {
                if (generated.ContainsKey(entryKey))
                    continue; // Already in generated — transpiler's version wins.
                if (managedKeys is not null && managedKeys.Contains(entryKey))
                    continue; // Stale transpiler key — drop it.
                generated[entryKey] = entryValue?.DeepClone();
            }

            // Replace in-place to preserve key ordering in the JSON output.
            // Clear the existing object and copy generated entries into it.
            foreach (var k in existing.Select(e => e.Key).ToList())
                existing.Remove(k);
            foreach (var (entryKey, entryValue) in generated)
                existing[entryKey] = entryValue?.DeepClone();
            // existing is already parented to root at the correct position.
            return;
        }

        root[key] = generated;
    }

    /// <summary>
    /// Builds the <c>imports</c> object with conditional exports for the <c>#/*</c> alias.
    /// Format: dist (.js + .d.ts) is preferred, source .ts is the fallback for dev.
    /// </summary>
    private static JsonObject BuildImports(
        string srcRelative,
        string distRelative,
        bool hasRootIndex,
        string outputPrefix
    )
    {
        var src = NormalizePath(srcRelative).TrimEnd('/');
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var distBase = outputPrefix.Length > 0 ? $"{dist}/{outputPrefix}" : dist;

        var imports = new JsonObject
        {
            ["#/*"] = new JsonObject
            {
                ["types"] = $"./{distBase}/*.d.ts",
                ["import"] = $"./{distBase}/*.js",
                ["default"] = $"./{src}/*.ts",
            },
        };

        if (hasRootIndex)
        {
            imports["#"] = new JsonObject
            {
                ["types"] = $"./{distBase}/index.d.ts",
                ["import"] = $"./{distBase}/index.js",
                ["default"] = $"./{src}/index.ts",
            };
        }

        return imports;
    }

    /// <summary>
    /// Builds the <c>exports</c> object listing the public subpaths the consumer can import.
    /// Only namespace barrel files (<c>index.ts</c>) become export entries — individual type
    /// files are accessed through their barrel, matching the namespace-first convention
    /// from ADR-0006. When <paramref name="outputPrefix"/> is non-empty, it is prepended
    /// to both the subpath key and the dist path.
    /// </summary>
    private static JsonObject BuildExports(
        IReadOnlyList<TsSourceFile> files,
        string distRelative,
        string outputPrefix
    )
    {
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var distBase = outputPrefix.Length > 0 ? $"{dist}/{outputPrefix}" : dist;
        var exports = new JsonObject();

        var barrels = files
            .Select(f => NormalizePath(f.FileName))
            .Where(name => Path.GetFileName(name).Equals("index.ts", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        foreach (var name in barrels)
        {
            var withoutExt = name[..^3]; // Strip .ts

            // Barrel directory: "issues/domain/index" → "issues/domain", root "index" → ""
            var parent = Path.GetDirectoryName(withoutExt)?.Replace('\\', '/') ?? "";

            // Subpath: prepend outputPrefix when present.
            // root index.ts with prefix "domain" → "./domain"
            // users/index.ts with prefix "domain" → "./domain/users"
            string subpath;
            if (outputPrefix.Length > 0)
                subpath = parent.Length == 0 ? $"./{outputPrefix}" : $"./{outputPrefix}/{parent}";
            else
                subpath = parent.Length == 0 ? "." : $"./{parent}";

            exports[subpath] = new JsonObject
            {
                ["types"] = $"./{distBase}/{withoutExt}.d.ts",
                ["import"] = $"./{distBase}/{withoutExt}.js",
            };
        }

        return exports;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('.', '/');
}
