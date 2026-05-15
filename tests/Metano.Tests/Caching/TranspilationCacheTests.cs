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
            ConfigurationFingerprint: "namespaceBarrels=False;stripInterfacePrefix=False",
            SourceHashes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/proj/src/Foo.cs"] = "abc123",
            },
            ReferenceFingerprints: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/refs/Bar.dll"] = "12345:6789",
            },
            AssetSourceHashes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/proj/schema.sql"] = "asset-hash-1",
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
        await Assert
            .That(roundTripped.ConfigurationFingerprint)
            .IsEqualTo("namespaceBarrels=False;stripInterfacePrefix=False");
        await Assert.That(roundTripped.SourceHashes["/proj/src/Foo.cs"]).IsEqualTo("abc123");
        await Assert
            .That(roundTripped.ReferenceFingerprints["/refs/Bar.dll"])
            .IsEqualTo("12345:6789");
        await Assert
            .That(roundTripped.AssetSourceHashes["/proj/schema.sql"])
            .IsEqualTo("asset-hash-1");
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
              "configurationFingerprint": "",
              "sourceHashes": {},
              "referenceFingerprints": {},
              "assetSourceHashes": {},
              "outputHashes": {}
            }
            """
        );

        var cache = TranspilationCache.TryRead(dir);
        await Assert.That(cache).IsNull();
    }

    [Test]
    public async Task TryRead_ReturnsNull_WhenRequiredSectionMissing()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(
            Path.Combine(dir, TranspilationCache.FileName),
            $$"""
            {
              "formatVersion": {{TranspilationCache.CurrentFormatVersion}},
              "target": "TypeScript"
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
        var withSamePrefix = CacheKeyBuilder.HashGeneratedContent(
            files,
            prefixBlock: "// header\n"
        );
        var withDifferentPrefix = CacheKeyBuilder.HashGeneratedContent(
            files,
            prefixBlock: "// changed\n"
        );

        await Assert.That(firstRun["a.ts"]).IsEqualTo(secondRun["a.ts"]);
        await Assert.That(withSamePrefix["a.ts"]).IsNotEqualTo(firstRun["a.ts"]);
        await Assert.That(withSamePrefix["a.ts"]).IsNotEqualTo(withDifferentPrefix["a.ts"]);
    }

    [Test]
    public async Task ValidateAndRehydrate_ReturnsNull_WhenFileMissing()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "alive.ts"), "x");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alive.ts"] = Sha256("x"),
            ["missing.ts"] = "anything",
        };

        var result = CacheKeyBuilder.ValidateAndRehydrate(dir, expected, prefixBlock: null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ValidateAndRehydrate_ReturnsNull_WhenFileModified()
    {
        var dir = MakeTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "edited.ts"), "original");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["edited.ts"] = Sha256("not-the-current-content"),
        };

        var result = CacheKeyBuilder.ValidateAndRehydrate(dir, expected, prefixBlock: null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ValidateAndRehydrate_ReturnsFiles_WhenAllHashesMatch()
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

        var rehydrated = CacheKeyBuilder.ValidateAndRehydrate(dir, hashes, prefixBlock: null);

        await Assert.That(rehydrated).IsNotNull();
        await Assert.That(rehydrated!.Count).IsEqualTo(2);
        await Assert.That(rehydrated.Any(f => f.Content == "alpha")).IsTrue();
        await Assert.That(rehydrated.Any(f => f.Content == "beta")).IsTrue();
    }

    [Test]
    public async Task ValidateAndRehydrate_StripsPrefix_FromOnDiskContent()
    {
        var dir = MakeTempDir();
        var prefix = "// header\n";
        var raw = "export const x = 1;\n";
        await File.WriteAllTextAsync(Path.Combine(dir, "prefixed.ts"), prefix + raw);

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prefixed.ts"] = Sha256(prefix + raw),
        };

        var rehydrated = CacheKeyBuilder.ValidateAndRehydrate(dir, hashes, prefixBlock: prefix);

        await Assert.That(rehydrated).IsNotNull();
        await Assert.That(rehydrated![0].Content).IsEqualTo(raw);
    }

    [Test]
    public async Task ValidateAndRehydrate_RejectsRootedPath()
    {
        var dir = MakeTempDir();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/etc/passwd"] = "anything",
        };

        var result = CacheKeyBuilder.ValidateAndRehydrate(dir, hashes, prefixBlock: null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ValidateAndRehydrate_RejectsParentSegment()
    {
        var dir = MakeTempDir();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["../escape.ts"] = "anything",
        };

        var result = CacheKeyBuilder.ValidateAndRehydrate(dir, hashes, prefixBlock: null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ComputeAssetFingerprints_ReturnsHashKeyedByCacheKey()
    {
        var dir = MakeTempDir();
        var assetPath = Path.Combine(dir, "schema.sql");
        await File.WriteAllTextAsync(assetPath, "CREATE TABLE t (id INTEGER);");

        var hashes = CacheKeyBuilder.ComputeAssetFingerprints([
            new AssetSpec(assetPath, RelativeDestination: "provider/schema.sql"),
        ]);

        await Assert.That(hashes.Count).IsEqualTo(1);
        await Assert
            .That(hashes.Keys.Single())
            .Contains("schema.sql")
            .And.Contains("=>provider/schema.sql");
    }

    [Test]
    public async Task ComputeAssetFingerprints_SkipsMissingFiles()
    {
        var dir = MakeTempDir();
        var present = Path.Combine(dir, "present.sql");
        var missing = Path.Combine(dir, "missing.sql");
        await File.WriteAllTextAsync(present, "CREATE TABLE t (id INTEGER);");

        var hashes = CacheKeyBuilder.ComputeAssetFingerprints([
            new AssetSpec(missing),
            new AssetSpec(present),
        ]);

        await Assert.That(hashes.Count).IsEqualTo(1);
        await Assert.That(hashes.Keys.Single()).Contains("present.sql");
    }

    [Test]
    public async Task ComputeAssetFingerprints_DistinguishesDestinations()
    {
        var dir = MakeTempDir();
        var source = Path.Combine(dir, "schema.sql");
        await File.WriteAllTextAsync(source, "x");

        var hashes = CacheKeyBuilder.ComputeAssetFingerprints([
            new AssetSpec(source, RelativeDestination: "a.sql"),
            new AssetSpec(source, RelativeDestination: "b.sql"),
        ]);

        await Assert.That(hashes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ComputeAssetFingerprints_ChangesWhenContentChanges()
    {
        var dir = MakeTempDir();
        var asset = Path.Combine(dir, "schema.sql");
        await File.WriteAllTextAsync(asset, "v1");

        var first = CacheKeyBuilder.ComputeAssetFingerprints([new AssetSpec(asset)]);

        await File.WriteAllTextAsync(asset, "v2");
        var second = CacheKeyBuilder.ComputeAssetFingerprints([new AssetSpec(asset)]);

        await Assert.That(first.Values.Single()).IsNotEqualTo(second.Values.Single());
    }

    private string MakeTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "metano-cache-test-" + Guid.NewGuid().ToString("N")
        );
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
