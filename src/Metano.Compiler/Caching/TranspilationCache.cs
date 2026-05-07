using System.Text.Json;

namespace Metano.Compiler.Caching;

/// <summary>
/// On-disk cache the transpiler writes after every successful run and
/// inspects at the start of the next run to decide whether anything
/// regenerates. The MVP scope (#21 PR 3a) is "skip the entire pipeline
/// when nothing changed"; per-group skip lands in the follow-up
/// (PR 3b) once a file-descriptor abstraction exists.
///
/// <para>
/// Cache invariants:
/// </para>
/// <list type="bullet">
///   <item><b>Source hashes</b>: SHA-256 of every C# syntax tree the
///   compilation parsed. Catches body edits the type-level dependency
///   graph alone would miss (ADR-0018 leaves bodies out of the
///   signature surface).</item>
///   <item><b>Reference fingerprints</b>: <c>length:lastWriteTimeUtc</c>
///   per metadata reference. Picks up assembly swaps without reading
///   the .dll bytes.</item>
///   <item><b>Output hashes</b>: SHA-256 of every file the previous
///   run emitted into the output directory. If a user (or
///   <c>--clean</c> on a sibling target, or a manual <c>rm</c>) deletes
///   a file we treat the cache as stale and regenerate.</item>
/// </list>
///
/// <para>
/// The cache file is stored at <c>&lt;outputDir&gt;/.metano-cache.json</c>
/// so it lives next to the artifacts it describes; <c>--clean</c>
/// wipes it alongside the outputs it pinned.
/// </para>
/// </summary>
public sealed record TranspilationCache(
    int FormatVersion,
    string Target,
    IReadOnlyDictionary<string, string> SourceHashes,
    IReadOnlyDictionary<string, string> ReferenceFingerprints,
    IReadOnlyDictionary<string, string> OutputHashes
)
{
    public const int CurrentFormatVersion = 1;
    public const string FileName = ".metano-cache.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static TranspilationCache? TryRead(string outputDir)
    {
        var path = Path.Combine(outputDir, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var cache = JsonSerializer.Deserialize<TranspilationCache>(json, JsonOptions);
            return cache?.FormatVersion == CurrentFormatVersion ? cache : null;
        }
        catch
        {
            // A corrupt cache file is treated as a miss — the next
            // run rebuilds from scratch and overwrites the bad file.
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
