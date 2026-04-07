using System.Diagnostics;
using ConsoleAppFramework;
using MetaSharp.Transformation;
using MetaSharp.TypeScript;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

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
        var projectPath = Path.GetFullPath(project);
        var outputDir = Path.GetFullPath(output);

        if (!File.Exists(projectPath))
        {
            Console.Error.WriteLine($"Project not found: {projectPath}");
            Environment.Exit(1);
            return;
        }

        var totalSw = Stopwatch.StartNew();

        Console.WriteLine($"MetaSharp: Loading project {Path.GetFileName(projectPath)}...");

        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"  Workspace error: {e.Diagnostic.Message}");
        });

        var compileSw = Stopwatch.StartNew();

        Console.WriteLine("  Opening MSBuild project...");
        var proj = await workspace.OpenProjectAsync(projectPath);
        Console.WriteLine("  Project loaded.");
        Console.WriteLine("  Creating Roslyn compilation...");
        var compilation = await proj.GetCompilationAsync();
        Console.WriteLine("  Compilation created.");

        compileSw.Stop();

        if (compilation is null)
        {
            Console.Error.WriteLine("Failed to compile project.");
            Environment.Exit(1);
            return;
        }

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"Compilation has {errors.Count} error(s):");
            foreach (var error in errors.Take(10))
                Console.Error.WriteLine($"  {error}");
            Environment.Exit(1);
            return;
        }

        if (time)
            Console.WriteLine($"  Compilation: {compileSw.ElapsedMilliseconds}ms");

        var transpileSw = Stopwatch.StartNew();

        var transformer = new TypeTransformer(compilation);
        var files = transformer.TransformAll();

        transpileSw.Stop();

        if (time)
            Console.WriteLine($"  Transpilation: {transpileSw.ElapsedMilliseconds}ms");

        // Report transpiler diagnostics (warnings about unsupported features, etc.)
        var diagnostics = transformer.Diagnostics;
        var errorCount = 0;
        var warningCount = 0;
        foreach (var diag in diagnostics)
        {
            switch (diag.Severity)
            {
                case Compiler.Diagnostics.MetaSharpDiagnosticSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"  {diag.Format()}");
                    Console.ResetColor();
                    errorCount++;
                    break;
                case Compiler.Diagnostics.MetaSharpDiagnosticSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine($"  {diag.Format()}");
                    Console.ResetColor();
                    warningCount++;
                    break;
                default:
                    Console.WriteLine($"  {diag.Format()}");
                    break;
            }
        }
        if (warningCount > 0 || errorCount > 0)
            Console.WriteLine($"MetaSharp: {warningCount} warning(s), {errorCount} error(s).");

        if (errorCount > 0)
        {
            Environment.Exit(1);
            return;
        }

        if (files.Count == 0)
        {
            Console.WriteLine("MetaSharp: No transpilable types found.");
            return;
        }

        var emitSw = Stopwatch.StartNew();

        if (clean && Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
            Console.WriteLine($"  Cleaned: {outputDir}");
        }

        Directory.CreateDirectory(outputDir);
        var printer = new Printer();

        foreach (var file in files)
        {
            var content = printer.Print(file);
            var filePath = Path.Combine(outputDir, file.FileName.Replace('/', Path.DirectorySeparatorChar));
            var fileDir = Path.GetDirectoryName(filePath);
            if (fileDir is not null) Directory.CreateDirectory(fileDir);
            await File.WriteAllTextAsync(filePath, content);
            Console.WriteLine($"  Generated: {file.FileName}");
        }

        emitSw.Stop();

        // Update package.json with imports/exports/sideEffects
        if (!skipPackageJson)
        {
            var resolvedPackageRoot = packageRoot is not null
                ? Path.GetFullPath(packageRoot)
                : Path.GetDirectoryName(outputDir)!;

            PackageJsonWriter.UpdateOrCreate(resolvedPackageRoot, outputDir, files, dist);
            Console.WriteLine($"  Updated: {Path.Combine(resolvedPackageRoot, "package.json")}");
        }

        totalSw.Stop();

        if (time)
        {
            Console.WriteLine($"  Emit: {emitSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Total: {totalSw.ElapsedMilliseconds}ms");
        }

        Console.WriteLine($"MetaSharp: {files.Count} file(s) generated in {outputDir}");
    }
}
