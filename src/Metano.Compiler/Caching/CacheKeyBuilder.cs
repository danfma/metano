using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Caching;

/// <summary>
/// Computes the cache fingerprint for a transpilation run. Builds source-file
/// SHA-256 hashes from the Roslyn syntax trees, reference fingerprints from
/// the metadata reference file paths (size + last-write-time tuple — fast and
/// good enough to spot a swap or rebuild), and on-disk output hashes by
/// re-hashing the files the previous run pinned in the cache. Output reads
/// stream through <see cref="SHA256.HashData(Stream)"/> so a 50 MB generated
/// file does not balloon into a managed string.
/// </summary>
public static class CacheKeyBuilder
{
    public static IReadOnlyDictionary<string, string> ComputeSourceHashes(Compilation compilation)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            // SyntaxTree.FilePath can be empty for in-memory trees (the test
            // harness uses these); skip those — they cannot collide with
            // anything on disk and cannot be re-read on a subsequent run.
            if (string.IsNullOrEmpty(tree.FilePath))
                continue;
            var checksum = tree.GetText().GetChecksum();
            // Roslyn computes the checksum from the underlying SourceText
            // stream incrementally — no full-file string materialization.
            hashes[NormalizePath(tree.FilePath)] = HexEncode(checksum.AsSpan());
        }
        return hashes;
    }

    public static IReadOnlyDictionary<string, string> ComputeReferenceFingerprints(
        Compilation compilation
    )
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in compilation.References)
        {
            if (reference is not PortableExecutableReference pe || pe.FilePath is null)
                continue;
            var info = new FileInfo(pe.FilePath);
            if (!info.Exists)
                continue;
            // length:ticks is precise enough for the cache: a recompile
            // touches LastWriteTime, a swap typically changes the length
            // too. Cheaper than re-hashing every .dll on every run.
            fingerprints[NormalizePath(pe.FilePath)] =
                $"{info.Length.ToString(CultureInfo.InvariantCulture)}:"
                + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        return fingerprints;
    }

    /// <summary>
    /// Hashes each generated file's eventual on-disk content (i.e., with
    /// the optional file-prefix block prepended, mirroring what
    /// <see cref="TranspilerHost"/> writes). The returned map is what
    /// gets pinned into the next run's <c>outputHashes</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> HashGeneratedContent(
        IReadOnlyList<GeneratedFile> files,
        string? prefixBlock
    )
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var content = string.IsNullOrEmpty(prefixBlock)
                ? file.Content
                : prefixBlock + file.Content;
            hashes[file.RelativePath] = HashUtf8(content);
        }
        return hashes;
    }

    /// <summary>
    /// In one disk pass per cached output: validates that every cached
    /// path is safe (no rooted, no <c>..</c>) and points at a file with
    /// the recorded hash, while collecting <see cref="GeneratedFile"/>
    /// entries for the host to return on a cache hit. Returns
    /// <see langword="null"/> on first mismatch / missing file / bad path
    /// — the host treats that as a cache miss and falls through to the
    /// full pipeline.
    /// </summary>
    public static IReadOnlyList<GeneratedFile>? ValidateAndRehydrate(
        string outputDir,
        IReadOnlyDictionary<string, string> outputHashes,
        string? prefixBlock
    )
    {
        var rehydrated = new List<GeneratedFile>(outputHashes.Count);
        foreach (var (relativePath, expectedHash) in outputHashes)
        {
            if (!IsSafeRelativePath(relativePath))
                return null;
            var fullPath = ResolveOutputPath(outputDir, relativePath);
            if (!File.Exists(fullPath))
                return null;

            // Single read: hash + UTF-8 decode from the same byte
            // buffer. The earlier shape opened the file twice
            // (stream-hash then ReadAllText); for a cache check over
            // dozens of generated files that doubled the I/O budget.
            var bytes = File.ReadAllBytes(fullPath);
            var actualHash = HexEncode(SHA256.HashData(bytes));
            if (actualHash != expectedHash)
                return null;
            var content = System.Text.Encoding.UTF8.GetString(bytes);
            // Strip the prefix block so rehydrated GeneratedFile.Content
            // matches what target.Transform would produce — the host
            // re-applies the prefix at write time on the full-pipeline
            // path, so callers reading TranspileResult.Files see the
            // same shape regardless of cache hit / miss.
            if (!string.IsNullOrEmpty(prefixBlock) && content.StartsWith(prefixBlock))
                content = content[prefixBlock.Length..];
            rehydrated.Add(new GeneratedFile(relativePath, content));
        }
        return rehydrated;
    }

    /// <summary>
    /// Fingerprints each declared asset by SHA-256 of its on-disk
    /// source content, keyed by the cache key from
    /// <see cref="ComputeAssetCacheKey"/>. Missing source files are
    /// silently omitted — the host surfaces an
    /// <see cref="DiagnosticCodes.AssetCopyFailure"/> when it tries
    /// to copy them, and the omitted entry guarantees the next run
    /// re-attempts the copy instead of short-circuiting on a stale
    /// fingerprint.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ComputeAssetFingerprints(
        IReadOnlyList<AssetSpec> assets
    )
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            if (!File.Exists(asset.Source))
                continue;
            var bytes = File.ReadAllBytes(asset.Source);
            fingerprints[ComputeAssetCacheKey(asset)] = HexEncode(SHA256.HashData(bytes));
        }
        return fingerprints;
    }

    /// <summary>
    /// Cache key shape: <c>NormalizePath(source)</c> for the mirror-
    /// default, or <c>NormalizePath(source)=&gt;relativeDestination</c>
    /// for an explicit <c>OutputPath</c> override. The destination
    /// participates so the same source emitted to two destinations
    /// produces two distinct rows — invalidating either one triggers
    /// a re-copy of only the affected destination.
    /// </summary>
    private static string ComputeAssetCacheKey(AssetSpec asset) =>
        asset.RelativeDestination is null
            ? NormalizePath(asset.Source)
            : $"{NormalizePath(asset.Source)}=>{asset.RelativeDestination}";

    public static bool DictionariesEqual(
        IReadOnlyDictionary<string, string> a,
        IReadOnlyDictionary<string, string> b
    )
    {
        if (a.Count != b.Count)
            return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other))
                return false;
            if (!string.Equals(value, other, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Rejects rooted paths and any segment equal to <c>..</c> so a
    /// hand-edited cache file cannot point <c>OutputsStillValid</c> /
    /// <c>RehydrateFiles</c> at arbitrary disk locations.
    /// </summary>
    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;
        if (Path.IsPathRooted(relativePath))
            return false;
        foreach (var segment in relativePath.Split('/', '\\'))
        {
            if (segment == "..")
                return false;
        }
        return true;
    }

    private static string ResolveOutputPath(string outputDir, string relativePath) =>
        Path.Combine(outputDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string HashUtf8(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return HexEncode(hash);
    }

    private static string HexEncode(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    private static string NormalizePath(string path) => Path.GetFullPath(path);
}
