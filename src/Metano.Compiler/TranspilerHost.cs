using System.Diagnostics;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Metano.Compiler;

/// <summary>
/// Target-agnostic orchestration for transpilation runs:
/// loads a C# project via MSBuildWorkspace, runs an <see cref="ITranspilerTarget"/>
/// against the resulting compilation, prints diagnostics, and writes generated files
/// to the output directory.
///
/// Each language target (TypeScript, Dart, …) wraps this in its own CLI which adds
/// target-specific flags (e.g., TypeScript's --dist, --skip-package-json) and any
/// post-emit work such as writing a package.json.
/// </summary>
public static class TranspilerHost
{
    public static async Task<TranspileResult> RunAsync(
        TranspileOptions options,
        ITranspilerTarget target
    )
    {
        var projectPath = Path.GetFullPath(options.ProjectPath);
        var outputDir = Path.GetFullPath(options.OutputDir);

        if (!File.Exists(projectPath))
        {
            Console.Error.WriteLine($"Project not found: {projectPath}");

            return new TranspileResult(false, [], 0, 1);
        }

        var totalSw = Stopwatch.StartNew();
        var compileSw = Stopwatch.StartNew();
        var (compilation, roslynErrorCount) = await LoadCompilationAsync(projectPath);

        compileSw.Stop();

        if (compilation is null)
            return new TranspileResult(false, [], 0, roslynErrorCount);

        if (options.ShowTimings)
            Console.WriteLine($"  Compilation: {compileSw.ElapsedMilliseconds}ms");

        var transpileSw = Stopwatch.StartNew();
        var output = target.Transform(compilation);

        transpileSw.Stop();

        if (options.ShowTimings)
            Console.WriteLine($"  Transpilation: {transpileSw.ElapsedMilliseconds}ms");

        var (warningCount, errorCount) = ReportDiagnostics(output.Diagnostics);

        if (errorCount > 0)
            return new TranspileResult(false, output.Files, warningCount, errorCount);

        if (output.Files.Count == 0)
        {
            Console.WriteLine("Metano: No transpilable types found.");

            return new TranspileResult(true, output.Files, warningCount, 0);
        }

        var emitSw = Stopwatch.StartNew();

        await EmitFilesAsync(outputDir, output.Files, options.Clean);
        emitSw.Stop();
        totalSw.Stop();

        if (options.ShowTimings)
        {
            Console.WriteLine($"  Emit: {emitSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Total: {totalSw.ElapsedMilliseconds}ms");
        }

        Console.WriteLine($"Metano: {output.Files.Count} file(s) generated in {outputDir}");

        return new TranspileResult(true, output.Files, warningCount, 0);
    }

    private static async Task<(Compilation? Compilation, int ErrorCount)> LoadCompilationAsync(
        string projectPath
    )
    {
        Console.WriteLine($"Metano: Loading project {Path.GetFileName(projectPath)}...");

        using var workspace = MSBuildWorkspace.Create();

        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"  Workspace error: {e.Diagnostic.Message}");
        });

        Console.WriteLine("  Opening MSBuild project...");

        var proj = await workspace.OpenProjectAsync(projectPath);

        Console.WriteLine("  Project loaded.");
        Console.WriteLine("  Creating Roslyn compilation...");

        var compilation = await proj.GetCompilationAsync();

        Console.WriteLine("  Compilation created.");

        if (compilation is null)
        {
            Console.Error.WriteLine("Failed to compile project.");

            return (null, 1);
        }

        var roslynErrors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (roslynErrors.Count > 0)
        {
            Console.Error.WriteLine($"Compilation has {roslynErrors.Count} error(s):");

            foreach (var error in roslynErrors.Take(10))
            {
                Console.Error.WriteLine($"  {error}");
            }

            return (null, roslynErrors.Count);
        }

        return (compilation, 0);
    }

    private static async Task EmitFilesAsync(
        string outputDir,
        IReadOnlyList<GeneratedFile> files,
        bool clean
    )
    {
        if (clean && Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
            Console.WriteLine($"  Cleaned: {outputDir}");
        }

        Directory.CreateDirectory(outputDir);

        foreach (var file in files)
        {
            var filePath = Path.Combine(
                outputDir,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar)
            );

            var fileDir = Path.GetDirectoryName(filePath);

            if (fileDir is not null)
                Directory.CreateDirectory(fileDir);

            await File.WriteAllTextAsync(filePath, file.Content);

            Console.WriteLine($"  Generated: {file.RelativePath}");
        }
    }

    private static (int Warnings, int Errors) ReportDiagnostics(
        IReadOnlyList<MetanoDiagnostic> diagnostics
    )
    {
        var errorCount = 0;
        var warningCount = 0;

        foreach (var diag in diagnostics)
        {
            switch (diag.Severity)
            {
                case MetanoDiagnosticSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"  {diag.Format()}");
                    Console.ResetColor();
                    errorCount++;
                    break;

                case MetanoDiagnosticSeverity.Warning:
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
            Console.WriteLine($"Metano: {warningCount} warning(s), {errorCount} error(s).");

        return (warningCount, errorCount);
    }
}
