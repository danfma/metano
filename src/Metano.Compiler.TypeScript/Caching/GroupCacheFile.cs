using System.Text.Json;

namespace Metano.Compiler.TypeScript.Caching;

/// <summary>
/// Per-target persistence for the per-group skip cache (PR 3c).
/// Lives next to <c>TranspilationCache</c>'s <c>.metano-cache.json</c>
/// at <c>&lt;outputDir&gt;/.metano-cache-groups-typescript.json</c>;
/// shape is keyed by the group key the
/// <c>TypeScript</c> transformer uses for grouping
/// (<c>namespace + fileName</c>).
///
/// <para>
/// A group entry is valid for re-use only when ALL of the
/// following hold: (a) the cached <c>ConfigurationFingerprint</c>
/// matches the active run's, (b) the cached closure hash matches
/// the closure currently computed from the dep graph, and (c) the
/// disk content's SHA-256 matches the cached
/// <see cref="CachedFileMetadata.ContentHash"/>. The whole-build
/// cache (PR 3a) verifies disk content on the short-circuit
/// path, but on a normal transform run it does not — so this
/// layer hashes the disk content itself before trusting the
/// cached metadata.
/// </para>
/// </summary>
public sealed record GroupCacheFile(
    int FormatVersion,
    string ConfigurationFingerprint,
    IReadOnlyDictionary<string, GroupCacheEntry> Groups
)
{
    public const int CurrentFormatVersion = 2;
    public const string FileName = ".metano-cache-groups-typescript.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static GroupCacheFile? TryRead(string outputDir)
    {
        var path = Path.Combine(outputDir, FileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            var cache = JsonSerializer.Deserialize<GroupCacheFile>(json, JsonOptions);
            if (cache is null)
                return null;
            if (cache.FormatVersion != CurrentFormatVersion)
                return null;
            if (cache.Groups is null || cache.ConfigurationFingerprint is null)
                return null;
            return cache;
        }
        catch
        {
            return null;
        }
    }

    public void Write(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public sealed record GroupCacheEntry(string ClosureHash, IReadOnlyList<CachedFileMetadata> Files);
