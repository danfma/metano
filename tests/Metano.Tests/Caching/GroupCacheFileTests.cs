using Metano.Compiler.TypeScript.Caching;

namespace Metano.Tests.Caching;

/// <summary>
/// Pin the on-disk format-version guard on
/// <see cref="GroupCacheFile"/>. The v2 → v3 bump (#220) added
/// <see cref="CachedExportKind"/>; a stale v2 cache silently
/// deserialising under v3 code would default every <c>kind</c> to
/// <c>Class</c> and misclassify every type-only export.
/// </summary>
public class GroupCacheFileTests
{
    private readonly List<string> _tempDirs = new();

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task TryRead_RejectsStaleV2File()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(
            Path.Combine(dir, GroupCacheFile.FileName),
            """
            {
              "formatVersion": 2,
              "configurationFingerprint": "fp",
              "groups": {}
            }
            """
        );

        var cache = GroupCacheFile.TryRead(dir);
        await Assert.That(cache).IsNull();
    }

    [Test]
    public async Task RoundTrip_PersistsCachedExportKindByName()
    {
        var dir = MakeTempDir();
        var original = new GroupCacheFile(
            FormatVersion: GroupCacheFile.CurrentFormatVersion,
            ConfigurationFingerprint: "fp",
            Groups: new Dictionary<string, GroupCacheEntry>(StringComparer.Ordinal)
            {
                ["g"] = new GroupCacheEntry(
                    "closure-hash",
                    [
                        new CachedFileMetadata(
                            "foo.ts",
                            "deadbeef",
                            [],
                            [new CachedExport("Foo", CachedExportKind.TypeAlias)]
                        ),
                    ]
                ),
            }
        );

        original.Write(dir);
        var raw = await File.ReadAllTextAsync(Path.Combine(dir, GroupCacheFile.FileName));

        // String-enum serialisation: caches survive enum-value reordering.
        await Assert.That(raw).Contains("\"TypeAlias\"");

        var roundTripped = GroupCacheFile.TryRead(dir);
        await Assert.That(roundTripped).IsNotNull();
        await Assert
            .That(roundTripped!.Groups["g"].Files[0].Exports[0].Kind)
            .IsEqualTo(CachedExportKind.TypeAlias);
    }

    private string MakeTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "metano-group-cache-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
