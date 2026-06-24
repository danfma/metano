using Metano.Compiler;
using Metano.Compiler.Caching;

namespace Metano.Tests.Caching;

/// <summary>
/// Pins self-cleaning incremental builds (spec 005 / ADR-0025): the host
/// reconciles the prior cache manifest against the current output set and removes
/// orphaned generated files (and emptied directories) — only files it produced,
/// never hand-written ones, and never when there is no manifest to reconcile.
/// </summary>
public class OrphanPruningTests
{
    private readonly List<string> _tempDirs = new();

    [After(Test)]
    public void Cleanup()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Prune_RemovesOrphan_KeepsCurrentAndHandWritten()
    {
        var outputDir = MakeOutputWithPriorManifest(
            priorFiles: ["app/widget.ts", "app/index.ts"],
            handWritten: ["app/keep.md"]
        );

        // This run no longer emits app/widget.ts.
        TranspilerHost.PruneOrphanedOutputs(outputDir, [Generated("app/index.ts")], Options());

        await Assert.That(Exists(outputDir, "app/widget.ts")).IsFalse();
        await Assert.That(Exists(outputDir, "app/index.ts")).IsTrue();
        await Assert.That(Exists(outputDir, "app/keep.md")).IsTrue();
    }

    [Test]
    public async Task Prune_IdenticalOutputSet_DeletesNothing()
    {
        var outputDir = MakeOutputWithPriorManifest(
            priorFiles: ["app/widget.ts", "app/index.ts"],
            handWritten: []
        );

        TranspilerHost.PruneOrphanedOutputs(
            outputDir,
            [Generated("app/widget.ts"), Generated("app/index.ts")],
            Options()
        );

        await Assert.That(Exists(outputDir, "app/widget.ts")).IsTrue();
        await Assert.That(Exists(outputDir, "app/index.ts")).IsTrue();
    }

    [Test]
    public async Task Prune_RemovesEmptiedDirectory()
    {
        var outputDir = MakeOutputWithPriorManifest(
            priorFiles: ["app/users/person.ts", "app/users/index.ts", "app/widget.ts"],
            handWritten: []
        );

        // The whole app/users namespace is gone this run.
        TranspilerHost.PruneOrphanedOutputs(outputDir, [Generated("app/widget.ts")], Options());

        await Assert.That(Directory.Exists(Path.Combine(outputDir, "app", "users"))).IsFalse();
        await Assert.That(Exists(outputDir, "app/widget.ts")).IsTrue();
    }

    [Test]
    public async Task Prune_NoCache_SkipsReconciliation_OrphanSurvives()
    {
        var outputDir = MakeOutputWithPriorManifest(priorFiles: ["app/widget.ts"], handWritten: []);

        TranspilerHost.PruneOrphanedOutputs(
            outputDir,
            [Generated("app/other.ts")],
            Options() with
            {
                NoCache = true,
            }
        );

        // With --no-cache there is no manifest to reconcile, so the orphan is left alone.
        await Assert.That(Exists(outputDir, "app/widget.ts")).IsTrue();
    }

    [Test]
    public async Task Prune_EmptyCurrentSet_RemovesAllPriorOrphans()
    {
        var outputDir = MakeOutputWithPriorManifest(
            priorFiles: ["app/widget.ts", "app/index.ts"],
            handWritten: ["keep.md"]
        );

        // The last transpilable type was removed — this run emits nothing.
        TranspilerHost.PruneOrphanedOutputs(outputDir, [], Options());

        await Assert.That(Exists(outputDir, "app/widget.ts")).IsFalse();
        await Assert.That(Exists(outputDir, "app/index.ts")).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(outputDir, "app"))).IsFalse();
        await Assert.That(Exists(outputDir, "keep.md")).IsTrue();
    }

    [Test]
    public async Task Prune_ManifestEscapeKey_NeverDeletesOutsideOutputRoot()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "metano-prune-test-" + Guid.NewGuid().ToString("N")
        );
        var outputDir = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(outputDir);
        _tempDirs.Add(tempRoot);

        // A file just outside the output tree that a corrupted `..` manifest key targets.
        var sibling = Path.Combine(tempRoot, "secret.txt");
        File.WriteAllText(sibling, "do not delete");
        File.WriteAllText(Path.Combine(outputDir, "widget.ts"), "// content\n");

        new TranspilationCache(
            FormatVersion: TranspilationCache.CurrentFormatVersion,
            Target: "TypeScript",
            ConfigurationFingerprint: "test",
            SourceHashes: new Dictionary<string, string>(),
            ReferenceFingerprints: new Dictionary<string, string>(),
            AssetSourceHashes: new Dictionary<string, string>(),
            OutputHashes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["../secret.txt"] = "hash",
                ["widget.ts"] = "hash",
            }
        ).Write(outputDir);

        // The run drops widget.ts; the `..` escape key must be ignored, not deleted.
        TranspilerHost.PruneOrphanedOutputs(outputDir, [], Options());

        await Assert.That(File.Exists(sibling)).IsTrue();
        await Assert.That(Exists(outputDir, "widget.ts")).IsFalse();
    }

    private static GeneratedFile Generated(string path) => new(path, "// content\n");

    private static TranspileOptions Options() => new(ProjectPath: "x.csproj", OutputDir: "out");

    private static bool Exists(string outputDir, string relativePath) =>
        File.Exists(
            Path.Combine(outputDir, relativePath.Replace('/', Path.DirectorySeparatorChar))
        );

    private string MakeOutputWithPriorManifest(string[] priorFiles, string[] handWritten)
    {
        var outputDir = Path.Combine(
            Path.GetTempPath(),
            "metano-prune-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(outputDir);
        _tempDirs.Add(outputDir);

        foreach (var rel in priorFiles.Concat(handWritten))
        {
            var disk = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(disk)!);
            File.WriteAllText(disk, "// content\n");
        }

        // The manifest records only the generated files — never the hand-written ones.
        var manifest = priorFiles.ToDictionary(path => path, _ => "hash", StringComparer.Ordinal);
        new TranspilationCache(
            FormatVersion: TranspilationCache.CurrentFormatVersion,
            Target: "TypeScript",
            ConfigurationFingerprint: "test",
            SourceHashes: new Dictionary<string, string>(),
            ReferenceFingerprints: new Dictionary<string, string>(),
            AssetSourceHashes: new Dictionary<string, string>(),
            OutputHashes: manifest
        ).Write(outputDir);

        return outputDir;
    }
}
