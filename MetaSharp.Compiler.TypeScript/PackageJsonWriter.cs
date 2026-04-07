using System.Text.Json;
using System.Text.Json.Nodes;
using MetaSharp.TypeScript.AST;

namespace MetaSharp;

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
    public static void UpdateOrCreate(
        string packageRoot,
        string outputDirAbsolute,
        IReadOnlyList<TsSourceFile> files,
        string distDirRelativeToPackageRoot = "./dist")
    {
        var packageJsonPath = Path.Combine(packageRoot, "package.json");
        var srcRelative = NormalizePath(Path.GetRelativePath(packageRoot, outputDirAbsolute));

        // Build the imports/exports objects
        var imports = BuildImports(srcRelative, distDirRelativeToPackageRoot);
        var exports = BuildExports(files, distDirRelativeToPackageRoot);

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
                ["name"] = Path.GetFileName(packageRoot.TrimEnd('/', '\\')),
                ["private"] = true,
            };
        }

        // Apply controlled fields (overwrite)
        root["type"] = "module";
        root["sideEffects"] = false;
        root["imports"] = imports;
        root["exports"] = exports;

        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(packageJsonPath, root.ToJsonString(WriteOptions) + "\n");
    }

    /// <summary>
    /// Builds the `imports` object with conditional exports for the `#/*` alias.
    /// Format: dist (.js + .d.ts) is preferred, source .ts is the fallback for dev.
    /// </summary>
    private static JsonObject BuildImports(string srcRelative, string distRelative)
    {
        var src = NormalizePath(srcRelative).TrimEnd('/');
        var dist = NormalizePath(distRelative).TrimEnd('/');

        return new JsonObject
        {
            ["#/*"] = new JsonObject
            {
                ["types"] = $"./{dist}/*.d.ts",
                ["import"] = $"./{dist}/*.js",
                ["default"] = $"./{src}/*.ts",
            }
        };
    }

    /// <summary>
    /// Builds the `exports` object listing every public path the consumer can import.
    /// Each barrel becomes a directory-level subpath, each individual file gets its own subpath.
    /// </summary>
    private static JsonObject BuildExports(IReadOnlyList<TsSourceFile> files, string distRelative)
    {
        var dist = NormalizePath(distRelative).TrimEnd('/');
        var exports = new JsonObject();

        // Sort for deterministic output
        var ordered = files.OrderBy(f => f.FileName, StringComparer.Ordinal).ToList();

        foreach (var file in ordered)
        {
            var name = NormalizePath(file.FileName);
            // Strip .ts
            var withoutExt = name.EndsWith(".ts") ? name[..^3] : name;

            string subpath;
            if (Path.GetFileName(withoutExt) == "index")
            {
                // Barrel: ./issues/domain/index.ts → "./issues/domain"
                var parent = Path.GetDirectoryName(withoutExt)?.Replace('\\', '/') ?? "";
                subpath = parent.Length == 0 ? "." : $"./{parent}";
            }
            else
            {
                // Regular file: ./issues/domain/issue.ts → "./issues/domain/issue"
                subpath = $"./{withoutExt}";
            }

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
