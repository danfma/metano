using System.Diagnostics;
using Metano.Compiler.Diagnostics;

namespace Metano.Compiler;

/// <summary>
/// Target-agnostic orchestration for transpilation runs: delegates project
/// loading + semantic extraction to an <see cref="ISourceFrontend"/>
/// (the C# frontend by default), runs an <see cref="ITranspilerTarget"/>
/// against the resulting compilation, prints diagnostics, and writes
/// generated files to the output directory.
///
/// Each language target (TypeScript, Dart, …) wraps this in its own CLI
/// which adds target-specific flags (e.g., TypeScript's --dist,
/// --skip-package-json) and any post-emit work such as writing a
/// package.json.
/// </summary>
public static class TranspilerHost
{
    public static Task<TranspileResult> RunAsync(
        TranspileOptions options,
        ITranspilerTarget target
    ) => RunAsync(options, target, new CSharpSourceFrontend());

    public static async Task<TranspileResult> RunAsync(
        TranspileOptions options,
        ITranspilerTarget target,
        ISourceFrontend frontend
    )
    {
        var projectPath = Path.GetFullPath(options.ProjectPath);
        var outputDir = Path.GetFullPath(options.OutputDir);

        var totalSw = Stopwatch.StartNew();
        var compileSw = Stopwatch.StartNew();
        var ir = await frontend.ExtractAsync(projectPath, target: target.Language);
        var compilation = frontend.LoadedCompilation;
        var roslynErrorCount = frontend.LoadErrorCount;

        compileSw.Stop();

        if (compilation is null)
        {
            // Surface the frontend's load-failure diagnostics via the
            // standard reporter so the CLI shows the MS0009 line(s)
            // alongside the raw stderr trace the frontend already wrote.
            var (frontendWarnings, frontendErrors) = ReportDiagnostics(ir.Diagnostics);
            var errors = frontendErrors > 0 ? frontendErrors : roslynErrorCount;
            return new TranspileResult(false, [], frontendWarnings, errors);
        }

        if (options.ShowTimings)
            Console.WriteLine($"  Compilation: {compileSw.ElapsedMilliseconds}ms");

        var transpileSw = Stopwatch.StartNew();
        var output = target.Transform(ir, compilation);

        transpileSw.Stop();

        if (options.ShowTimings)
            Console.WriteLine($"  Transpilation: {transpileSw.ElapsedMilliseconds}ms");

        // Merge frontend diagnostics with target diagnostics so any
        // warnings raised during extraction are surfaced even on the
        // happy path.
        var allDiagnostics =
            ir.Diagnostics.Count == 0
                ? output.Diagnostics
                : ir.Diagnostics.Concat(output.Diagnostics).ToList();
        var (warningCount, errorCount) = ReportDiagnostics(allDiagnostics);

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
