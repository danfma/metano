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
        var resolvedSrcRoot = NormalizePath(srcRoot ?? srcRelative.Split('/')[0]);

        // Output prefix: the path from the source root to the output directory.
        // Empty when outputDir IS the source root (e.g., srcRelative = "src").
        var outputPrefix =
            srcRelative == resolvedSrcRoot
                ? ""
                : NormalizePath(Path.GetRelativePath(resolvedSrcRoot, srcRelative));

        // Build the imports/exports objects. Exports are only needed for libraries.
        var exports = isExecutable
            ? null
            : BuildExports(files, distDirRelativeToPackageRoot, outputPrefix);
        var rootExportKey = outputPrefix.Length > 0 ? $"./{outputPrefix}" : ".";
        var hasRootIndex = exports?.ContainsKey(rootExportKey) ?? false;
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

        // [EmitPackage] is the source of truth for the package name when present.
        if (authoritativePackageName is not null)
        {
            var existingName = root["name"]?.GetValue<string>();
            if (existingName is not null && existingName != authoritativePackageName)
            {
                diagnostics.Add(
                    new MetanoDiagnostic(
                        MetanoDiagnosticSeverity.Warning,
                        DiagnosticCodes.CrossPackageResolution,
                        $"package.json#name '{existingName}' diverges from "
                            + $"[assembly: EmitPackage(\"{authoritativePackageName}\")]. "
                            + $"Overwriting with the attribute value — consumers will import via "
                            + $"'{authoritativePackageName}'."
                    )
                );
            }
            root["name"] = authoritativePackageName;
        }

        // Apply controlled fields
        root["type"] = "module";
        root["sideEffects"] = false;

        // Merge transpiler-managed entries into the existing imports/exports objects.
        // User-defined entries for other subpaths are preserved.
        MergeJsonObject(root, "imports", imports);

        if (exports is not null)
            MergeJsonObject(root, "exports", exports);
        else
            root.Remove("exports");

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
    /// Merges entries from <paramref name="generated"/> into the existing JSON object at
    /// <paramref name="key"/>. Creates the object if it doesn't exist. Overwrites entries
    /// with matching keys but preserves user-defined entries with other keys.
    /// </summary>
    private static void MergeJsonObject(JsonObject root, string key, JsonObject generated)
    {
        var existing = root[key] as JsonObject ?? new JsonObject();

        foreach (var (entryKey, entryValue) in generated)
        {
            // Remove existing entry first (JsonObject doesn't allow duplicate keys)
            existing.Remove(entryKey);
            existing[entryKey] = entryValue?.DeepClone();
        }

        root[key] = existing;
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
