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
/// A group entry is valid for re-use when every output file it
/// declares still matches the global <c>outputHashes</c> map kept
/// by <see cref="Metano.Compiler.Caching.TranspilationCache"/> —
/// the whole-build cache (PR 3a) verifies that map at the host
/// layer before <c>target.Transform</c> ever runs, so the per-group
/// hit path can trust it here.
/// </para>
/// </summary>
public sealed record GroupCacheFile(
    int FormatVersion,
    IReadOnlyDictionary<string, GroupCacheEntry> Groups
)
{
    public const int CurrentFormatVersion = 1;
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
            if (cache.Groups is null)
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
