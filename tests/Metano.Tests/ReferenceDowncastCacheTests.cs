using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.Caching;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Tests;

/// <summary>
/// The import-graph decision of a reference cast (#669) under the per-group
/// cache: a cached group records the classes its casts check, so a later change
/// that puts it on an import cycle transforms it again only to assert its cast,
/// and a group that asserted one is not cached, so the decision follows the
/// graph of every run.
/// </summary>
public class ReferenceDowncastCacheTests
{
    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task GroupCache_FollowsTheImportCycleOfEveryRun()
    {
        var cacheDir = CreateTempDir();

        var acyclic = TransformWithCache(cacheDir, Nodes(describeCallsPrinter: false));
        var cyclic = TransformWithCache(cacheDir, Nodes(describeCallsPrinter: true));
        var acyclicAgain = TransformWithCache(cacheDir, Nodes(describeCallsPrinter: false));

        await Assert.That(acyclic).Contains("castTo<Leaf>(n, Leaf).text");
        await Assert.That(cyclic).Contains("(n as Leaf).text");
        await Assert.That(acyclicAgain).Contains("castTo<Leaf>(n, Leaf).text");
    }

    [Test]
    public async Task GroupCache_RecordsTheClassesAGroupsCastsCheck()
    {
        var cacheDir = CreateTempDir();

        TransformWithCache(cacheDir, Nodes(describeCallsPrinter: false));

        var printer = GroupCacheFile
            .TryRead(cacheDir)!
            .Groups.Values.Single(entry => entry.Files[0].Path == "app/printer.ts");

        await Assert.That(printer.CheckedCasts).IsEquivalentTo(["Leaf"]);
    }

    /// <summary>
    /// Transforms <paramref name="source"/> with the per-group cache in
    /// <paramref name="cacheDir"/>, writes the files there the way the host
    /// would, and returns <c>app/printer.ts</c> as written.
    /// </summary>
    private static string TransformWithCache(string cacheDir, string source)
    {
        var compilation = TranspileHelper.CompileLibrary(source);
        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);
        var printer = new Printer();

        var transformer = new TypeTransformer(ir, compilation)
        {
            CacheOutputDir = cacheDir,
            CacheConfigurationFingerprint = "fp",
        };

        foreach (var file in transformer.TransformAll())
        {
            var path = Path.Combine(cacheDir, file.FileName);

            var content = transformer.CachedFileContents.TryGetValue(file.FileName, out var cached)
                ? cached
                : printer.Print(file);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return File.ReadAllText(Path.Combine(cacheDir, "app", "printer.ts"));
    }

    private static string Nodes(bool describeCallsPrinter) =>
        $$"""
            namespace App;

            [Transpile]
            public abstract class Node
            {
                public string Describe() => {{(
                describeCallsPrinter ? "Printer.Print(this)" : "\"node\""
            )}};
            }

            [Transpile]
            public static class Printer
            {
                public static string Print(Node n) => "leaf:" + ((Leaf)n).Text;
            }

            [Transpile]
            public class Leaf : Node
            {
                public string Text = "t";
            }
            """;

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-cast-cycle-{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }
}
