using System.Text.Json;
using System.Text.Json.Nodes;
using Metano.Compiler.Diagnostics;
using Metano.TypeScript.AST;

namespace Metano;

/// <summary>
/// Generates and updates the consumer's package.json with `imports`, `exports`, `sideEffects`,
/// and `type` fields based on the TypeScript files emitted by the transpiler.
///
/// Strategy: non-destructive merge. The user's hand-written fields (name, dependencies,
/// scripts, etc.) are preserved. Only the controlled fields are overwritten.
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
    /// <param name="distDirRelativeToPackageRoot">Relative path from packageRoot to the JS output directory (default: "./dist").</param>
    /// <param name="files">All generated TsSourceFile objects (type files + barrels).</param>
    /// <param name="authoritativePackageName">When non-null, this name (typically read
    /// from <c>[assembly: EmitPackage(...)]</c>) is written to <c>package.json#name</c>
    /// as the source of truth. If the existing file already had a different name, an
    /// MS0007 diagnostic is returned and the authoritative value still wins (because
    /// cross-package import resolution depends on it).</param>
    /// <param name="crossPackageDependencies">Maps each cross-package npm name that the
    /// transpiler emitted an import for, to its version specifier (e.g., <c>^1.2.3</c>
    /// or <c>workspace:*</c>). The writer MERGES these into <c>package.json#dependencies</c>
    /// — existing entries the user has hand-written for OTHER packages are preserved
    /// untouched; existing entries for the same package are overwritten with the
    /// compiler-computed version (so versions stay in sync with the C# project).</param>
    /// <returns>List of diagnostics raised while writing — empty in the happy path.</returns>
    public static IReadOnlyList<MetanoDiagnostic> UpdateOrCreate(
        string packageRoot,
        string outputDirAbsolute,
        IReadOnlyList<TsSourceFile> files,
        string distDirRelativeToPackageRoot = "./dist",
        string? authoritativePackageName = null,
        IReadOnlyDictionary<string, string>? crossPackageDependencies = null,
        bool isExecutable = false
    )
    {
        var diagnostics = new List<MetanoDiagnostic>();
        var packageJsonPath = Path.Combine(packageRoot, "package.json");
        var srcRelative = NormalizePath(Path.GetRelativePath(packageRoot, outputDirAbsolute));

        // Build the imports/exports objects. Exports are only needed for libraries;
        // the root-index check is derived from the exports object to avoid a duplicate scan.
        var exports = isExecutable ? null : BuildExports(files, distDirRelativeToPackageRoot);
        var hasRootIndex = exports?.ContainsKey(".") ?? false;
        var imports = BuildImports(srcRelative, distDirRelativeToPackageRoot, hasRootIndex);

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
        // If the existing file's name diverges, warn and overwrite — cross-package
        // import resolution depends on the attribute value, so the package.json must
        // match what consumers will write in their `import { … } from "<name>/…"` lines.
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

        // Only populate imports when the user hasn't customized them.
        if (root["imports"] is null)
            root["imports"] = imports;
        // Exports are always overwritten — they're a transpiler-computed artifact
        // derived from the namespace barrel structure. Stale exports from previous
        // runs must not survive. Executables don't need exports (they're not
        // consumed by other packages), so any stale entries are removed.
        if (exports is not null)
            root["exports"] = exports;
        else
            root.Remove("exports");

        // Merge auto-generated cross-package dependencies into the existing
        // `dependencies` object, leaving any user-hand-written entries for OTHER
        // packages alone. We only touch the keys we know about, so adding `react` or
        // `bun-types` by hand will survive a regenerate cycle.
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
    /// Builds the `imports` object with conditional exports for the `#/*` alias.
    /// Format: dist (.js + .d.ts) is preferred, source .ts is the fallback for dev.
    /// </summary>
    private static JsonObject BuildImports(
        string srcRelative,
        string distRelative,
        bool hasRootIndex
    )
    {
        var src = NormalizePath(srcRelative).TrimEnd('/');
        var dist = NormalizePath(distRelative).TrimEnd('/');

        var imports = new JsonObject
        {
            ["#/*"] = new JsonObject
            {
                ["types"] = $"./{dist}/*.d.ts",
                ["import"] = $"./{dist}/*.js",
                ["default"] = $"./{src}/*.ts",
            },
        };

        if (hasRootIndex)
        {
            imports["#"] = new JsonObject
            {
                ["types"] = $"./{dist}/index.d.ts",
                ["import"] = $"./{dist}/index.js",
                ["default"] = $"./{src}/index.ts",
            };
        }

        return imports;
    }

    /// <summary>
    /// Builds the <c>exports</c> object listing the public subpaths the consumer can import.
    /// Only namespace barrel files (<c>index.ts</c>) become export entries — individual type
    /// files are accessed through their barrel, matching the namespace-first convention
    /// from ADR-0006.
    /// </summary>
    private static JsonObject BuildExports(IReadOnlyList<TsSourceFile> files, string distRelative)
    {
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var exports = new JsonObject();

        var barrels = files
            .Select(f => NormalizePath(f.FileName))
            .Where(name => Path.GetFileName(name).Equals("index.ts", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        foreach (var name in barrels)
        {
            var withoutExt = name[..^3]; // Strip .ts

            // Barrel: issues/domain/index → "./issues/domain", root index → "."
            var parent = Path.GetDirectoryName(withoutExt)?.Replace('\\', '/') ?? "";
            var subpath = parent.Length == 0 ? "." : $"./{parent}";

            exports[subpath] = new JsonObject
            {
                ["types"] = $"./{dist}/{withoutExt}.d.ts",
                ["import"] = $"./{dist}/{withoutExt}.js",
            };
        }

        return exports;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('.', '/');
}
