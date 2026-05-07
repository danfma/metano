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
/// re-hashing the files the previous run pinned in the cache.
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
            var text = tree.GetText().ToString();
            hashes[NormalizePath(tree.FilePath)] = Sha256(text);
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
            hashes[file.RelativePath] = Sha256(content);
        }
        return hashes;
    }

    public static bool OutputsStillValid(
        string outputDir,
        IReadOnlyDictionary<string, string> expectedHashes
    )
    {
        foreach (var (relativePath, expected) in expectedHashes)
        {
            if (!MatchesOnDisk(ResolveOutputPath(outputDir, relativePath), expected))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Reads each cached output file from disk and returns it as a
    /// <see cref="GeneratedFile"/>, so the host can populate
    /// <c>TranspileResult.Files</c> on a cache hit (callers that read it
    /// post-run keep seeing every emitted artifact, not an empty list).
    /// </summary>
    public static IReadOnlyList<GeneratedFile> RehydrateFiles(
        string outputDir,
        IReadOnlyDictionary<string, string> outputHashes
    )
    {
        var files = new List<GeneratedFile>(outputHashes.Count);
        foreach (var relativePath in outputHashes.Keys)
        {
            var path = ResolveOutputPath(outputDir, relativePath);
            if (File.Exists(path))
                files.Add(new GeneratedFile(relativePath, File.ReadAllText(path)));
        }
        return files;
    }

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

    private static bool MatchesOnDisk(string path, string expectedHash)
    {
        if (!File.Exists(path))
            return false;
        return Sha256(File.ReadAllText(path)) == expectedHash;
    }

    private static string ResolveOutputPath(string outputDir, string relativePath) =>
        Path.Combine(outputDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Sha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);
}
