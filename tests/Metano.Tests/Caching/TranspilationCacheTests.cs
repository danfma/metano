using Metano.Compiler;
using Metano.Compiler.Caching;

namespace Metano.Tests.Caching;

/// <summary>
/// Pin the on-disk shape of <see cref="TranspilationCache"/> and the helpers
/// in <see cref="CacheKeyBuilder"/> so PR 3a's whole-build short-circuit
/// (ADR-0021) stays correct as the schema evolves.
/// </summary>
public class TranspilationCacheTests
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
    public async Task RoundTrip_PreservesEveryHashSection()
    {
        var dir = MakeTempDir();
        var original = new TranspilationCache(
            FormatVersion: TranspilationCache.CurrentFormatVersion,
            Target: "TypeScript",
            SourceHashes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/proj/src/Foo.cs"] = "abc123",
            },
            ReferenceFingerprints: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/refs/Bar.dll"] = "12345:6789",
            },
            OutputHashes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/foo.ts"] = "def456",
            }
        );

        original.Write(dir);
        var roundTripped = TranspilationCache.TryRead(dir);

        await Assert.That(roundTripped).IsNotNull();
        await Assert.That(roundTripped!.FormatVersion).IsEqualTo(original.FormatVersion);
        await Assert.That(roundTripped.Target).IsEqualTo("TypeScript");
        await Assert.That(roundTripped.SourceHashes["/proj/src/Foo.cs"]).IsEqualTo("abc123");
        await Assert.That(roundTripped.ReferenceFingerprints["/refs/Bar.dll"])
            .IsEqualTo("12345:6789");
        await Assert.That(roundTripped.OutputHashes["src/foo.ts"]).IsEqualTo("def456");
    }

    [Test]
    public async Task TryRead_ReturnsNull_WhenCacheFileMissing()
    {
        var dir = MakeTempDir();
        var cache = TranspilationCache.TryRead(dir);
        await Assert.That(cache).IsNull();
    }

    [Test]
    public async Task TryRead_ReturnsNull_WhenFormatVersionMismatch()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(
            Path.Combine(dir, TranspilationCache.FileName),
            """
            {
              "formatVersion": 999,
              "target": "TypeScript",
              "sourceHashes": {},
              "referenceFingerprints": {},
              "outputHashes": {}
            }
            """
        );

        var cache = TranspilationCache.TryRead(dir);
        await Assert.That(cache).IsNull();
    }

    [Test]
    public async Task TryRead_ReturnsNull_WhenJsonCorrupt()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(
            Path.Combine(dir, TranspilationCache.FileName),
            "{ this is not valid json"
        );

        var cache = TranspilationCache.TryRead(dir);
        await Assert.That(cache).IsNull();
    }

    [Test]
    public async Task HashGeneratedContent_IsDeterministic_AndPrefixSensitive()
    {
        var files = new List<GeneratedFile>
        {
            new("a.ts", "export const x = 1;\n"),
            new("b.ts", "export const y = 2;\n"),
        };

        var firstRun = CacheKeyBuilder.HashGeneratedContent(files, prefixBlock: null);
        var secondRun = CacheKeyBuilder.HashGeneratedContent(files, prefixBlock: null);
        var withSamePrefix = CacheKeyBuilder.HashGeneratedContent(files, prefixBlock: "// header\n");
        var withDifferentPrefix = CacheKeyBuilder.HashGeneratedContent(files, prefixBlock: "// changed\n");

        await Assert.That(firstRun["a.ts"]).IsEqualTo(secondRun["a.ts"]);
        await Assert.That(withSamePrefix["a.ts"]).IsNotEqualTo(firstRun["a.ts"]);
        await Assert.That(withSamePrefix["a.ts"]).IsNotEqualTo(withDifferentPrefix["a.ts"]);
    }

    [Test]
    public async Task OutputsStillValid_DetectsMissingFile()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "alive.ts"), "x");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alive.ts"] = Sha256("x"),
            ["missing.ts"] = "anything",
        };

        await Assert.That(CacheKeyBuilder.OutputsStillValid(dir, expected)).IsFalse();
    }

    [Test]
    public async Task OutputsStillValid_DetectsModifiedFile()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "edited.ts"), "original");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["edited.ts"] = Sha256("not-the-current-content"),
        };

        await Assert.That(CacheKeyBuilder.OutputsStillValid(dir, expected)).IsFalse();
    }

    [Test]
    public async Task OutputsStillValid_TrueWhenAllHashesMatch()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "match.ts"), "match");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["match.ts"] = Sha256("match"),
        };

        await Assert.That(CacheKeyBuilder.OutputsStillValid(dir, expected)).IsTrue();
    }

    [Test]
    public async Task RehydrateFiles_ReadsContentFromDisk_PreservesRelativePathOrder()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "first.ts"), "alpha");
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        await File.WriteAllTextAsync(Path.Combine(dir, "nested", "second.ts"), "beta");

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["first.ts"] = Sha256("alpha"),
            ["nested/second.ts"] = Sha256("beta"),
        };

        var rehydrated = CacheKeyBuilder.RehydrateFiles(dir, hashes);

        await Assert.That(rehydrated.Count).IsEqualTo(2);
        await Assert.That(rehydrated[0].Content).IsEqualTo("alpha");
        await Assert.That(rehydrated[1].Content).IsEqualTo("beta");
    }

    private string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "metano-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static string Sha256(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
