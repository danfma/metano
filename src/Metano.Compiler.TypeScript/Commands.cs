using ConsoleAppFramework;
using Metano.Compiler;
using Metano.Compiler.Watch;

namespace Metano;

public class Commands
{
    /// <summary>
    /// Transpile C# types to TypeScript.
    /// </summary>
    /// <param name="project">-p, Path to the C# project file (.csproj)</param>
    /// <param name="output">-o, Output directory for generated TypeScript files</param>
    /// <param name="time">-t, Show compilation and transpilation timings</param>
    /// <param name="clean">-c, Clean output directory before generating</param>
    /// <param name="packageRoot">Root directory of the consumer package (default: parent of --output)</param>
    /// <param name="dist">Path (relative to packageRoot) where the JS build output lives (default: ./dist)</param>
    /// <param name="srcRoot">TypeScript source root relative to packageRoot (default: inferred from first segment of output path). Used to compute the dist prefix when output targets a subdirectory (e.g., src/domain/).</param>
    /// <param name="skipPackageJson">Skip generating/updating package.json</param>
    /// <param name="namespaceBarrels">Emit an additional src/index.ts root barrel mirroring the C# namespace hierarchy via `export namespace` blocks (opt-in; see ADR-0006).</param>
    /// <param name="stripInterfacePrefix">Drop the C# `I` prefix from generated interface names when it is followed by another uppercase letter (opt-in). Collisions with sibling types in the same namespace keep the prefix and emit MS0017.</param>
    /// <param name="filePrefix">Opaque text written verbatim at the top of every generated file, followed by a single newline. Use to inject formatter / lint directives (e.g., a Biome `biome-ignore-all` block, a Prettier `// @prettier-ignore`, a `// @generated` provenance marker).</param>
    /// <param name="dryRun">Run the full pipeline but do NOT write any files. Print a preflight summary (file count + total line count + per-file paths) instead. Useful for CI preflight checks and exploratory runs. Skips the package.json update too.</param>
    /// <param name="noCache">Disable the incremental cache. Forces a full transpilation even when sources, references, and outputs are byte-identical to the previous run.</param>
    /// <param name="watch">Stay running and re-transpile every time a .cs / .csproj / .props / .targets file under the project directory changes. Combine with the incremental cache (default) for instant re-runs when nothing actually changed.</param>
    [Command("")]
    public async Task Transpile(
        string project,
        string output,
        bool time = false,
        bool clean = false,
        string? packageRoot = null,
        string dist = "./dist",
        string? srcRoot = null,
        bool skipPackageJson = false,
        bool namespaceBarrels = false,
        bool stripInterfacePrefix = false,
        string? filePrefix = null,
        bool dryRun = false,
        bool noCache = false,
        bool watch = false
    )
    {
        var target = new TypeScriptTarget
        {
            NamespaceBarrels = namespaceBarrels,
            StripInterfacePrefix = stripInterfacePrefix,
        };

        var options = new TranspileOptions(
            ProjectPath: project,
            OutputDir: output,
            ShowTimings: time,
            Clean: clean,
            FilePrefix: filePrefix,
            DryRun: dryRun,
            NoCache: noCache
        );

        async Task RunOnce()
        {
            var result = await TranspilerHost.RunAsync(options, target);

            if (!result.Success)
            {
                if (!watch)
                    Environment.Exit(1);
                return;
            }

            // Target-specific post-emit: write/merge the consumer's package.json so the
            // generated barrels are exposed via subpath imports/exports.
            // Dry-run skips the disk write to keep "no files modified" honest.
            if (!skipPackageJson && !dryRun && target.LastSourceFiles.Count > 0)
            {
                var outputDir = Path.GetFullPath(output);

                var resolvedPackageRoot = packageRoot is not null
                    ? Path.GetFullPath(packageRoot)
                    : FindPackageRoot(outputDir);

                var pkgDiagnostics = PackageJsonWriter.UpdateOrCreate(
                    resolvedPackageRoot,
                    outputDir,
                    target.LastSourceFiles,
                    dist,
                    authoritativePackageName: target.LastEmitPackageName,
                    crossPackageDependencies: target.LastCrossPackageDependencies,
                    isExecutable: target.LastIsExecutable,
                    srcRoot: srcRoot
                );

                foreach (var d in pkgDiagnostics)
                    Console.WriteLine(d.Format());

                Console.WriteLine(
                    $"  Updated: {Path.Combine(resolvedPackageRoot, "package.json")}"
                );
            }
        }

        if (watch)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            await WatchHost.RunAsync(project, RunOnce, cts.Token);
            return;
        }

        await RunOnce();
    }

    /// <summary>
    /// Walks up from <paramref name="startDir"/> looking for the nearest directory
    /// that contains a <c>package.json</c>. This mimics how npm/bun resolve the
    /// package root and removes the fragile assumption that the output directory is
    /// always a direct child of the package root (e.g., <c>&lt;root&gt;/src/</c>).
    /// Falls back to the parent of <paramref name="startDir"/> when no
    /// <c>package.json</c> is found (preserving the legacy behavior).
    /// </summary>
    private static string FindPackageRoot(string startDir)
    {
        var current = startDir;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "package.json")))
                return current;
            var parent = Path.GetDirectoryName(current);
            if (parent == current)
                break; // filesystem root
            current = parent;
        }
        // Fallback: parent of the output dir (legacy convention)
        return Path.GetDirectoryName(startDir)!;
    }
}
