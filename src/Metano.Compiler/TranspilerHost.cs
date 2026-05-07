using System.Diagnostics;
using Metano.Compiler.Caching;
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

        // Incremental cache short-circuit (#21 / ADR-0021): if every C#
        // syntax tree, every metadata reference, and every output file
        // matches the previously cached fingerprint, skip the rest of
        // the pipeline. --clean (which already wiped the output dir
        // including .metano-cache.json) and --no-cache opt out.
        var sourceHashes = CacheKeyBuilder.ComputeSourceHashes(compilation);
        var referenceFingerprints = CacheKeyBuilder.ComputeReferenceFingerprints(compilation);
        if (
            ShouldAttemptCache(options)
            && TryShortCircuitFromCache(
                outputDir,
                target.Language.ToString(),
                sourceHashes,
                referenceFingerprints,
                options.ShowTimings,
                totalSw,
                out var cachedFiles
            )
        )
        {
            return new TranspileResult(true, cachedFiles, 0, 0);
        }

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

        if (options.DryRun)
        {
            // Skip every disk write — print a preflight summary instead so
            // CI checks and exploratory runs can validate the pipeline
            // without touching the output tree.
            PrintDryRunSummary(outputDir, output.Files);
            emitSw.Stop();
            totalSw.Stop();
            if (options.ShowTimings)
                Console.WriteLine($"  Total: {totalSw.ElapsedMilliseconds}ms");
            return new TranspileResult(true, output.Files, warningCount, 0);
        }

        await EmitFilesAsync(outputDir, output.Files, options.Clean, options.FilePrefix);
        emitSw.Stop();
        totalSw.Stop();

        if (options.ShowTimings)
        {
            Console.WriteLine($"  Emit: {emitSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Total: {totalSw.ElapsedMilliseconds}ms");
        }

        Console.WriteLine($"Metano: {output.Files.Count} file(s) generated in {outputDir}");

        WriteCacheIfEnabled(
            options,
            target,
            outputDir,
            output.Files,
            sourceHashes,
            referenceFingerprints
        );

        return new TranspileResult(true, output.Files, warningCount, 0);
    }

    private static bool ShouldAttemptCache(TranspileOptions options) =>
        !options.NoCache && !options.DryRun && !options.Clean;

    private static void WriteCacheIfEnabled(
        TranspileOptions options,
        ITranspilerTarget target,
        string outputDir,
        IReadOnlyList<GeneratedFile> files,
        IReadOnlyDictionary<string, string> sourceHashes,
        IReadOnlyDictionary<string, string> referenceFingerprints
    )
    {
        if (options.NoCache || options.DryRun)
            return;
        var prefixBlock = string.IsNullOrEmpty(options.FilePrefix)
            ? null
            : options.FilePrefix + "\n";
        var outputHashes = CacheKeyBuilder.HashGeneratedContent(files, prefixBlock);
        var cache = new TranspilationCache(
            FormatVersion: TranspilationCache.CurrentFormatVersion,
            Target: target.Language.ToString(),
            SourceHashes: sourceHashes,
            ReferenceFingerprints: referenceFingerprints,
            OutputHashes: outputHashes
        );
        cache.Write(outputDir);
    }

    private static bool TryShortCircuitFromCache(
        string outputDir,
        string targetLanguage,
        IReadOnlyDictionary<string, string> sourceHashes,
        IReadOnlyDictionary<string, string> referenceFingerprints,
        bool showTimings,
        Stopwatch totalSw,
        out IReadOnlyList<GeneratedFile> cachedFiles
    )
    {
        cachedFiles = [];
        var cache = TranspilationCache.TryRead(outputDir);
        if (cache is null)
            return false;
        if (!string.Equals(cache.Target, targetLanguage, StringComparison.Ordinal))
            return false;
        if (!CacheKeyBuilder.DictionariesEqual(cache.SourceHashes, sourceHashes))
            return false;
        if (!CacheKeyBuilder.DictionariesEqual(cache.ReferenceFingerprints, referenceFingerprints))
            return false;
        if (!CacheKeyBuilder.OutputsStillValid(outputDir, cache.OutputHashes))
            return false;

        cachedFiles = CacheKeyBuilder.RehydrateFiles(outputDir, cache.OutputHashes);

        totalSw.Stop();
        Console.WriteLine(
            $"Metano: incremental cache hit — {cache.OutputHashes.Count} output file(s) reused, no work to do."
        );
        if (showTimings)
            Console.WriteLine($"  Total: {totalSw.ElapsedMilliseconds}ms");
        return true;
    }

    private static void PrintDryRunSummary(string outputDir, IReadOnlyList<GeneratedFile> files)
    {
        var totalLines = 0;
        foreach (var file in files)
            totalLines += CountLines(file.Content);

        Console.WriteLine(
            $"Metano (dry run): {files.Count} file(s), {totalLines} line(s) would be written to {outputDir}"
        );
        foreach (var file in files)
            Console.WriteLine($"  Would write: {file.RelativePath}");
    }

    private static int CountLines(string content)
    {
        if (content.Length == 0)
            return 0;
        var count = 1;
        for (var i = 0; i < content.Length; i++)
            if (content[i] == '\n')
                count++;
        return content[^1] == '\n' ? count - 1 : count;
    }

    private static async Task EmitFilesAsync(
        string outputDir,
        IReadOnlyList<GeneratedFile> files,
        bool clean,
        string? filePrefix
    )
    {
        if (clean && Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
            Console.WriteLine($"  Cleaned: {outputDir}");
        }

        Directory.CreateDirectory(outputDir);

        var prefixBlock = string.IsNullOrEmpty(filePrefix) ? "" : filePrefix + "\n";

        foreach (var file in files)
        {
            var filePath = Path.Combine(
                outputDir,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar)
            );

            var fileDir = Path.GetDirectoryName(filePath);

            if (fileDir is not null)
                Directory.CreateDirectory(fileDir);

            await File.WriteAllTextAsync(filePath, prefixBlock + file.Content);

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
