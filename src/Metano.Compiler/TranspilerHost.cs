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
        var projectDir = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        var assets = options.Assets ?? [];

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
        // syntax tree, every metadata reference, the target's emit-config
        // fingerprint, and every output file match the previously cached
        // fingerprint, skip the rest of the pipeline. --clean (which
        // already wiped the output dir including .metano-cache.json) and
        // --no-cache opt out. Hashes are computed lazily so a --no-cache
        // run does not pay the SHA bill.
        IReadOnlyDictionary<string, string>? sourceHashes = null;
        IReadOnlyDictionary<string, string>? referenceFingerprints = null;
        IReadOnlyDictionary<string, string>? assetSourceHashes = null;
        var configFingerprint = BuildConfigFingerprint(target, options);
        if (ShouldAttemptCache(options))
        {
            sourceHashes = CacheKeyBuilder.ComputeSourceHashes(compilation);
            referenceFingerprints = CacheKeyBuilder.ComputeReferenceFingerprints(compilation);
            assetSourceHashes = CacheKeyBuilder.ComputeAssetFingerprints(assets);
            if (
                TryShortCircuitFromCache(
                    outputDir,
                    target.Language.ToString(),
                    configFingerprint,
                    sourceHashes,
                    referenceFingerprints,
                    assetSourceHashes,
                    options.FilePrefix,
                    options.ShowTimings,
                    totalSw,
                    out var cachedFiles
                )
            )
            {
                return new TranspileResult(true, cachedFiles, 0, 0);
            }
        }

        var transpileSw = Stopwatch.StartNew();
        // Pass outputDir to the target only when the run will actually
        // touch disk — dry-run and --no-cache must not write the
        // per-target group cache (PR 3c).
        var targetCacheDir = options.DryRun || options.NoCache ? null : outputDir;
        var output = target.Transform(ir, compilation, targetCacheDir, options.FilePrefix);

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
        var assetDiagnostics = await CopyAssetsAsync(assets, outputDir, projectDir);
        var (assetWarnings, assetErrors) =
            assetDiagnostics.Count > 0 ? ReportDiagnostics(assetDiagnostics) : (0, 0);
        warningCount += assetWarnings;
        if (assetErrors > 0)
            return new TranspileResult(false, output.Files, warningCount, assetErrors);
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
            configFingerprint,
            sourceHashes,
            referenceFingerprints,
            assetSourceHashes,
            assets,
            compilation
        );

        return new TranspileResult(true, output.Files, warningCount, 0);
    }

    private static bool ShouldAttemptCache(TranspileOptions options) =>
        !options.NoCache && !options.DryRun && !options.Clean;

    private static string BuildConfigFingerprint(
        ITranspilerTarget target,
        TranspileOptions options
    ) =>
        $"target={target.ConfigurationFingerprint};filePrefix={options.FilePrefix ?? string.Empty}";

    private static void WriteCacheIfEnabled(
        TranspileOptions options,
        ITranspilerTarget target,
        string outputDir,
        IReadOnlyList<GeneratedFile> files,
        string configFingerprint,
        IReadOnlyDictionary<string, string>? sourceHashes,
        IReadOnlyDictionary<string, string>? referenceFingerprints,
        IReadOnlyDictionary<string, string>? assetSourceHashes,
        IReadOnlyList<AssetSpec> assets,
        Microsoft.CodeAnalysis.Compilation compilation
    )
    {
        if (options.NoCache || options.DryRun)
            return;
        var prefixBlock = string.IsNullOrEmpty(options.FilePrefix)
            ? null
            : options.FilePrefix + "\n";
        var outputHashes = CacheKeyBuilder.HashGeneratedContent(files, prefixBlock);
        // The read path may have skipped these on a --clean run (no cache
        // to consult). Compute now so the freshly written cache is
        // consultable on the next run.
        sourceHashes ??= CacheKeyBuilder.ComputeSourceHashes(compilation);
        referenceFingerprints ??= CacheKeyBuilder.ComputeReferenceFingerprints(compilation);
        assetSourceHashes ??= CacheKeyBuilder.ComputeAssetFingerprints(assets);
        var cache = new TranspilationCache(
            FormatVersion: TranspilationCache.CurrentFormatVersion,
            Target: target.Language.ToString(),
            ConfigurationFingerprint: configFingerprint,
            SourceHashes: sourceHashes,
            ReferenceFingerprints: referenceFingerprints,
            AssetSourceHashes: assetSourceHashes,
            OutputHashes: outputHashes
        );
        cache.Write(outputDir);
    }

    private static bool TryShortCircuitFromCache(
        string outputDir,
        string targetLanguage,
        string configFingerprint,
        IReadOnlyDictionary<string, string> sourceHashes,
        IReadOnlyDictionary<string, string> referenceFingerprints,
        IReadOnlyDictionary<string, string> assetSourceHashes,
        string? filePrefix,
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
        if (
            !string.Equals(
                cache.ConfigurationFingerprint,
                configFingerprint,
                StringComparison.Ordinal
            )
        )
            return false;
        if (!CacheKeyBuilder.DictionariesEqual(cache.SourceHashes, sourceHashes))
            return false;
        if (!CacheKeyBuilder.DictionariesEqual(cache.ReferenceFingerprints, referenceFingerprints))
            return false;
        if (!CacheKeyBuilder.DictionariesEqual(cache.AssetSourceHashes, assetSourceHashes))
            return false;

        var prefixBlock = string.IsNullOrEmpty(filePrefix) ? null : filePrefix + "\n";
        var rehydrated = CacheKeyBuilder.ValidateAndRehydrate(
            outputDir,
            cache.OutputHashes,
            prefixBlock
        );
        if (rehydrated is null)
            return false;

        cachedFiles = rehydrated;
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

    /// <summary>
    /// Counts logical lines in <paramref name="content"/>. A trailing
    /// newline does not introduce an extra empty line — files that
    /// end with <c>\n</c> count the same as those that do not.
    /// </summary>
    private static int CountLines(string content)
    {
        if (content.Length == 0)
            return 0;
        var newlines = content.AsSpan().Count('\n');
        return content[^1] == '\n' ? newlines : newlines + 1;
    }

    /// <summary>
    /// Copies <see cref="AssetSpec"/> entries declared on the run into
    /// <paramref name="outputDir"/>. Destination is the spec's
    /// <see cref="AssetSpec.RelativeDestination"/> when present;
    /// otherwise the source's path relative to <paramref name="projectDir"/>
    /// is mirrored under the output tree. Each copy is sandboxed: any
    /// resolved destination that escapes <paramref name="outputDir"/>
    /// (rooted path or <c>..</c> segment), missing source file, or any
    /// I/O failure surfaces as an
    /// <see cref="DiagnosticCodes.AssetCopyFailure"/> error so the
    /// build fails fast — a manifest bug that silently shipped a
    /// broken artifact would be far worse than a loud diagnostic.
    /// </summary>
    internal static async Task<IReadOnlyList<MetanoDiagnostic>> CopyAssetsAsync(
        IReadOnlyList<AssetSpec> assets,
        string outputDirFull,
        string projectDir
    )
    {
        if (assets.Count == 0)
            return [];

        var diagnostics = new List<MetanoDiagnostic>();
        foreach (var asset in assets)
            await CopyOneAssetAsync(asset, outputDirFull, projectDir, diagnostics);
        return diagnostics;
    }

    private static async Task CopyOneAssetAsync(
        AssetSpec asset,
        string outputDirFull,
        string projectDir,
        List<MetanoDiagnostic> diagnostics
    )
    {
        if (!File.Exists(asset.Source))
        {
            diagnostics.Add(CreateMissingAssetDiagnostic(asset.Source));
            return;
        }

        if (HasRootedDestination(asset.RelativeDestination))
        {
            diagnostics.Add(
                CreateEscapingAssetDiagnostic(asset.Source, asset.RelativeDestination!)
            );
            return;
        }

        var relative = ResolveAssetDestination(asset, projectDir);
        var destination = Path.GetFullPath(Path.Combine(outputDirFull, relative));
        if (!IsInsideOutputDir(destination, outputDirFull))
        {
            diagnostics.Add(CreateEscapingAssetDiagnostic(asset.Source, relative));
            return;
        }

        try
        {
            var destinationDir = Path.GetDirectoryName(destination);
            if (destinationDir is not null)
                Directory.CreateDirectory(destinationDir);
            await using var source = File.OpenRead(asset.Source);
            await using var sink = File.Create(destination);
            await source.CopyToAsync(sink);
            Console.WriteLine($"  Copied asset: {relative}");
        }
        catch (Exception ex)
        {
            diagnostics.Add(CreateAssetIoDiagnostic(asset.Source, relative, ex));
        }
    }

    /// <summary>
    /// Rooted destinations (<c>/abs/path</c>, <c>C:\path</c>) would
    /// silently relocate the asset outside the output tree once
    /// <c>Path.Combine</c> dropped the relative prefix. Reject them
    /// up-front so the user sees an explicit MS0025 diagnostic
    /// instead of either a confusing "escapes output directory"
    /// message or a silent rewrite.
    /// </summary>
    private static bool HasRootedDestination(string? relativeDestination) =>
        !string.IsNullOrEmpty(relativeDestination)
        && (Path.IsPathRooted(relativeDestination) || relativeDestination.StartsWith('/'));

    /// <summary>
    /// Resolves the output-relative destination for an asset. An
    /// explicit override (from <c>%(MetanoAsset.OutputPath)</c>) wins
    /// over the source-mirror heuristic. The fallback mirrors the
    /// asset's path relative to the consumer's project directory so a
    /// typical <c>&lt;MetanoAsset Include="schema.sql" /&gt;</c> lands
    /// at <c>OutputDir/schema.sql</c>, and a nested
    /// <c>assets/seed.json</c> preserves its <c>assets/</c> prefix.
    /// </summary>
    private static string ResolveAssetDestination(AssetSpec asset, string projectDir)
    {
        if (!string.IsNullOrEmpty(asset.RelativeDestination))
            return NormalizeRelative(asset.RelativeDestination);

        var sourceFull = Path.GetFullPath(asset.Source);
        var projectFull = Path.GetFullPath(projectDir);
        var mirrored = Path.GetRelativePath(projectFull, sourceFull);
        return NormalizeRelative(mirrored);
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/');

    private static bool IsInsideOutputDir(string destinationFull, string outputDirFull)
    {
        var outputWithSeparator = outputDirFull.EndsWith(Path.DirectorySeparatorChar)
            ? outputDirFull
            : outputDirFull + Path.DirectorySeparatorChar;
        return destinationFull.StartsWith(outputWithSeparator, StringComparison.Ordinal)
            || string.Equals(destinationFull, outputDirFull, StringComparison.Ordinal);
    }

    private static MetanoDiagnostic CreateMissingAssetDiagnostic(string source) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.AssetCopyFailure,
            $"Asset '{source}' was not found on disk. "
                + "Check the <MetanoAsset Include=\"...\"> path in the csproj."
        );

    private static MetanoDiagnostic CreateEscapingAssetDiagnostic(string source, string relative) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.AssetCopyFailure,
            $"Asset '{source}' resolved to '{relative}', which escapes the output "
                + "directory. <MetanoAsset OutputPath=\"...\"> must be a relative path "
                + "without leading slashes, drive letters, or '..' segments."
        );

    private static MetanoDiagnostic CreateAssetIoDiagnostic(
        string source,
        string relative,
        Exception ex
    ) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.AssetCopyFailure,
            $"Failed to copy asset '{source}' to '{relative}': {ex.Message}"
        );

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
