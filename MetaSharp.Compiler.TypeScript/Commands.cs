using ConsoleAppFramework;
using MetaSharp.Compiler;

namespace MetaSharp;

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
    /// <param name="skipPackageJson">Skip generating/updating package.json</param>
    [Command("")]
    public async Task Transpile(
        string project,
        string output,
        bool time = false,
        bool clean = false,
        string? packageRoot = null,
        string dist = "./dist",
        bool skipPackageJson = false)
    {
        var target = new TypeScriptTarget();
        var options = new TranspileOptions(
            ProjectPath: project,
            OutputDir: output,
            ShowTimings: time,
            Clean: clean
        );

        var result = await TranspilerHost.RunAsync(options, target);

        if (!result.Success)
        {
            Environment.Exit(1);
            return;
        }

        // Target-specific post-emit: write/merge the consumer's package.json so the
        // generated barrels are exposed via subpath imports/exports.
        if (!skipPackageJson && target.LastSourceFiles.Count > 0)
        {
            var outputDir = Path.GetFullPath(output);
            var resolvedPackageRoot = packageRoot is not null
                ? Path.GetFullPath(packageRoot)
                : Path.GetDirectoryName(outputDir)!;

            PackageJsonWriter.UpdateOrCreate(resolvedPackageRoot, outputDir, target.LastSourceFiles, dist);
            Console.WriteLine($"  Updated: {Path.Combine(resolvedPackageRoot, "package.json")}");
        }
    }
}
