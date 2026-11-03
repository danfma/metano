using Metano.Compiler;
using Metano.Compiler.Caching;
using Metano.Compiler.TypeScript.Caching;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests.Caching;

/// <summary>
/// Issue #433: upgrading Metano with unchanged sources must regenerate the output. Both
/// incremental caches pin the compiler release in their configuration fingerprint, so a
/// cache an older release wrote is never served, while one the running release wrote is.
/// An "older release" cache is the running release's own cache with its fingerprint
/// rewritten, which is exactly what an older compiler leaves behind.
/// </summary>
public class CompilerReleaseCacheTests
{
    private const string OlderRelease = "0.0.1-older";

    private const string StaleMarker = "// emitted by an older compiler\n";

    private const string NotesSource = """
        using Metano.Annotations;

        [assembly: TranspileAssembly]

        namespace Acme.Notes
        {
            public record Note(string Title);
        }
        """;

    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-release-cache-")
        .FullName;

    private string OutputDir => Path.Combine(_root, "src");

    private string ProjectPath => Path.Combine(_root, "Notes.csproj");

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task CacheWrittenByTheSameRelease_IsServed()
    {
        await Transpile();

        var servedFromCache = await Transpile();

        await Assert.That(servedFromCache).IsTrue();
    }

    [Test]
    public async Task CacheWrittenByAnotherRelease_IsNotServed()
    {
        await Transpile();
        RewriteReleaseInHostCache(OlderRelease);

        var servedAfterUpgrade = await Transpile();
        var servedOnTheNextRun = await Transpile();

        await Assert.That(servedAfterUpgrade).IsFalse();
        await Assert.That(servedOnTheNextRun).IsTrue();
    }

    [Test]
    public async Task GroupEmittedByAnotherRelease_IsRegenerated()
    {
        await Transpile();
        var freshContent = File.ReadAllText(NoteFilePath());
        PlantStaleNoteOutput();
        RewriteReleaseInHostCache(OlderRelease);
        RewriteReleaseInGroupCache(OlderRelease);

        await Transpile();

        await Assert.That(File.ReadAllText(NoteFilePath())).IsEqualTo(freshContent);
    }

    /// <summary>
    /// Control for <see cref="GroupEmittedByAnotherRelease_IsRegenerated"/>: the same
    /// planted output is reused when the running release wrote the group cache, so the
    /// regeneration above is caused by the release in the fingerprint alone.
    /// </summary>
    [Test]
    public async Task GroupEmittedByTheSameRelease_IsReused()
    {
        await Transpile();
        var staleContent = PlantStaleNoteOutput();

        await Transpile();

        await Assert.That(File.ReadAllText(NoteFilePath())).IsEqualTo(staleContent);
    }

    /// <summary>Runs the host once and returns whether it was served from the full cache.</summary>
    private async Task<bool> Transpile()
    {
        var recordingTarget = new CacheAdoptionRecorder(new TypeScriptTarget());
        var options = new TranspileOptions(ProjectPath, OutputDir);

        var result = await TranspilerHost.RunAsync(
            options,
            recordingTarget,
            new InMemorySourceFrontend(Compile())
        );

        if (!result.Success)
            throw new InvalidOperationException(
                $"Transpilation failed: {result.ErrorCount} error(s)."
            );

        return recordingTarget.AdoptedCachedOutput;
    }

    private CSharpCompilation Compile()
    {
        var tree = CSharpSyntaxTree.ParseText(
            NotesSource,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: Path.Combine(_root, "Note.cs")
        );

        return CSharpCompilation.Create(
            "Notes",
            [tree],
            TranspileHelper.BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    /// <summary>
    /// Replaces the generated <c>Note</c> file with content an older compiler could have
    /// emitted and records it in the group cache as that group's output, so the group is
    /// reusable unless its configuration fingerprint rules it out.
    /// </summary>
    private string PlantStaleNoteOutput()
    {
        var staleContent = StaleMarker + File.ReadAllText(NoteFilePath());
        var groupCache = ReadGroupCache();
        var (groupKey, entry) = NoteGroup(groupCache);

        var staleEntry = entry with
        {
            Files =
            [
                entry.Files[0] with
                {
                    ContentHash = FileMetadataExtractor.Sha256(staleContent),
                },
            ],
        };

        File.WriteAllText(NoteFilePath(), staleContent);

        var groups = new Dictionary<string, GroupCacheEntry>(groupCache.Groups)
        {
            [groupKey] = staleEntry,
        };

        (groupCache with { Groups = groups }).Write(OutputDir);

        return staleContent;
    }

    private void RewriteReleaseInHostCache(string release)
    {
        var cache =
            TranspilationCache.TryRead(OutputDir)
            ?? throw new InvalidOperationException("The host cache was not written.");

        var rewritten = cache with
        {
            ConfigurationFingerprint = WithRelease(cache.ConfigurationFingerprint, release),
        };

        rewritten.Write(OutputDir);
    }

    private void RewriteReleaseInGroupCache(string release)
    {
        var cache = ReadGroupCache();

        var rewritten = cache with
        {
            ConfigurationFingerprint = WithRelease(cache.ConfigurationFingerprint, release),
        };

        rewritten.Write(OutputDir);
    }

    private static string WithRelease(string fingerprint, string release)
    {
        var currentRelease = $"compiler={CompilerRelease.Version};";

        if (!fingerprint.StartsWith(currentRelease, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The fingerprint does not pin the running compiler release: {fingerprint}"
            );

        return $"compiler={release};" + fingerprint[currentRelease.Length..];
    }

    private GroupCacheFile ReadGroupCache() =>
        GroupCacheFile.TryRead(OutputDir)
        ?? throw new InvalidOperationException("The group cache was not written.");

    private static (string Key, GroupCacheEntry Entry) NoteGroup(GroupCacheFile cache)
    {
        var (key, entry) = cache.Groups.Single(group =>
            group.Value.Files[0].Path.EndsWith("note.ts", StringComparison.Ordinal)
        );

        return (key, entry);
    }

    private string NoteFilePath()
    {
        var (_, entry) = NoteGroup(ReadGroupCache());

        return Path.Combine(OutputDir, entry.Files[0].Path);
    }
}
