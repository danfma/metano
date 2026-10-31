using System.Text.Json.Nodes;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Tests;

/// <summary>
/// Spec 008 (#267): an opt-in <c>[EmitPackage(SubpathRoot = …)]</c> strips a namespace
/// prefix from <c>package.json#exports</c> keys and from every consumer's cross-package
/// specifier, while the on-disk layout and the <c>#/*</c> alias stay full-namespace.
/// </summary>
public class ExportsSubpathRootTests
{
    private const string Root = "VideoStudio.Domain";

    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task BarrelUnderRoot_ExportsStrippedKeyWithFullPathTargets()
    {
        var exports = WriteExports(Root, "video-studio/domain/episodes/index.ts");

        var entry = exports["./episodes"] as JsonObject;

        await Assert.That(exports.ContainsKey("./video-studio/domain/episodes")).IsFalse();
        await Assert.That(entry).IsNotNull();
        await Assert
            .That(entry!["source"]?.GetValue<string>())
            .IsEqualTo("./src/video-studio/domain/episodes/index.ts");
        await Assert
            .That(entry["types"]?.GetValue<string>())
            .IsEqualTo("./dist/video-studio/domain/episodes/index.d.ts");
        await Assert
            .That(entry["import"]?.GetValue<string>())
            .IsEqualTo("./dist/video-studio/domain/episodes/index.js");
    }

    [Test]
    public async Task BarrelOfRootNamespace_ExportsPackageRoot()
    {
        var exports = WriteExports(Root, "video-studio/domain/index.ts");

        await Assert.That(KeysOf(exports)).IsEquivalentTo(["."]);
        await Assert
            .That(exports["."]?["import"]?.GetValue<string>())
            .IsEqualTo("./dist/video-studio/domain/index.js");
    }

    [Test]
    public async Task BarrelOutsideRoot_KeepsFullPath()
    {
        var exports = WriteExports(Root, "shared/kernel/index.ts");

        await Assert.That(KeysOf(exports)).IsEquivalentTo(["./shared/kernel"]);
    }

    [Test]
    public async Task SharedStringPrefixWithoutSegmentBoundary_KeepsFullPath()
    {
        var exports = WriteExports(Root, "video-studio/domain-events/index.ts");

        await Assert.That(KeysOf(exports)).IsEquivalentTo(["./video-studio/domain-events"]);
    }

    [Test]
    public async Task NoRoot_KeepsFullNamespaceKeys()
    {
        var exports = WriteExports(
            subpathRoot: null,
            "video-studio/domain/index.ts",
            "video-studio/domain/episodes/index.ts"
        );

        await Assert
            .That(KeysOf(exports))
            .IsEquivalentTo(["./video-studio/domain", "./video-studio/domain/episodes"]);
    }

    [Test]
    public async Task OutputPrefix_IsPrependedAfterStripping()
    {
        var tempDir = CreateTempDir();
        var outputDir = Path.Combine(tempDir, "src", "domain");

        var diagnostics = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            outputDir,
            Barrels("video-studio/domain/index.ts", "video-studio/domain/episodes/index.ts"),
            srcRoot: "src",
            subpathRoot: Root
        );

        await Assert.That(diagnostics).IsEmpty();
        await Assert
            .That(KeysOf(ReadExports(tempDir)))
            .IsEquivalentTo(["./domain", "./domain/episodes"]);
    }

    [Test]
    public async Task Root_LeavesImportsAliasUnchanged()
    {
        var tempDir = CreateTempDir();

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            Path.Combine(tempDir, "src"),
            Barrels("video-studio/domain/episodes/index.ts"),
            subpathRoot: Root
        );

        var alias = ReadJson(tempDir)["imports"]?["#/*"] as JsonObject;

        await Assert.That(alias?["source"]?.GetValue<string>()).IsEqualTo("./src/*.ts");
        await Assert.That(alias?["import"]?.GetValue<string>()).IsEqualTo("./dist/*.js");
    }

    [Test]
    public async Task Root_PrunesFullPathKeysFromAPreviousRun()
    {
        var tempDir = CreateTempDir();
        var outputDir = Path.Combine(tempDir, "src");
        var files = Barrels("video-studio/domain/episodes/index.ts");

        PackageJsonWriter.UpdateOrCreate(tempDir, outputDir, files);
        PackageJsonWriter.UpdateOrCreate(tempDir, outputDir, files, subpathRoot: Root);

        await Assert.That(KeysOf(ReadExports(tempDir))).IsEquivalentTo(["./episodes"]);
    }

    [Test]
    public async Task RootAggregationBarrelAndRootNamespaceBarrel_CollideOnPackageRoot()
    {
        var tempDir = CreateTempDir();

        var diagnostics = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            Path.Combine(tempDir, "src"),
            Barrels("index.ts", "video-studio/domain/index.ts"),
            subpathRoot: Root
        );

        var collision = diagnostics.Single();

        await Assert.That(collision.Code).IsEqualTo(DiagnosticCodes.InvalidExportsSubpathRoot);
        await Assert.That(collision.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert
            .That(ReadExports(tempDir)["."]?["import"]?.GetValue<string>())
            .IsEqualTo("./dist/video-studio/domain/index.js");
    }

    [Test]
    public async Task StrippedKeyEqualToAnOutsideKey_UnderRootBarrelWins()
    {
        var tempDir = CreateTempDir();

        // Barrels are visited in ordinal order: the outside barrel ("shared/…") comes
        // first here and is then displaced by the barrel under the root.
        var diagnostics = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            Path.Combine(tempDir, "src"),
            Barrels("shared/index.ts", "video-studio/domain/shared/index.ts"),
            subpathRoot: Root
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .IsEquivalentTo([DiagnosticCodes.InvalidExportsSubpathRoot]);
        await Assert
            .That(ReadExports(tempDir)["./shared"]?["import"]?.GetValue<string>())
            .IsEqualTo("./dist/video-studio/domain/shared/index.js");
    }

    [Test]
    public async Task StrippedKeyClaimedFirst_OutsideBarrelIsLeftOut()
    {
        var tempDir = CreateTempDir();

        // Ordinal order visits "app/core/zeta/…" before "zeta/…", so the barrel under
        // the root claims the key first and the outside one is skipped.
        var diagnostics = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            Path.Combine(tempDir, "src"),
            Barrels("app/core/zeta/index.ts", "zeta/index.ts"),
            subpathRoot: "App.Core"
        );

        await Assert
            .That(diagnostics.Select(d => d.Code))
            .IsEquivalentTo([DiagnosticCodes.InvalidExportsSubpathRoot]);
        await Assert
            .That(ReadExports(tempDir)["./zeta"]?["import"]?.GetValue<string>())
            .IsEqualTo("./dist/app/core/zeta/index.js");
    }

    [Test]
    public async Task ConsumerImport_UsesStrippedSubpath()
    {
        var output = TranspileConsumerOf("VideoStudio.Domain.Episodes.Episode");

        await Assert
            .That(output)
            .Contains("import type { Episode } from \"@video-studio/domain/episodes\"");
    }

    [Test]
    public async Task ConsumerImport_OfRootNamespaceType_UsesBarePackage()
    {
        var output = TranspileConsumerOf("VideoStudio.Domain.Clock");

        await Assert.That(output).Contains("import type { Clock } from \"@video-studio/domain\"");
    }

    [Test]
    public async Task ConsumerImport_OutsideRoot_KeepsFullPath()
    {
        var output = TranspileConsumerOf("VideoStudio.DomainEvents.Published");

        await Assert
            .That(output)
            .Contains(
                "import type { Published } from \"@video-studio/domain/video-studio/domain-events\""
            );
    }

    [Test]
    public async Task ConsumerFunctionImport_FromNoContainerFacade_UsesStrippedSubpath()
    {
        var library = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@video-studio/domain", SubpathRoot = "VideoStudio.Domain")]

            namespace VideoStudio.Domain.Formatting;

            [NoContainer]
            public static class Format
            {
                public static string Shout(string text) => text + "!";
            }
            """;
        var consumer = """
            using VideoStudio.Domain.Formatting;

            [assembly: TranspileAssembly]

            namespace App;

            public static class Greeter
            {
                public static string Loud() => Format.Shout("hi");
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(library, consumer)["app/greeter.ts"];

        await Assert.That(output).Contains("from \"@video-studio/domain/formatting\"");
    }

    [Test]
    public async Task InvalidRoot_ReportsMs0029AndKeepsFullPaths()
    {
        var source = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@video-studio/domain", SubpathRoot = "Video Studio..Domain")]

            namespace VideoStudio.Domain;

            public record Clock(int Ticks);
            """;
        var compilation = TranspileHelper.CompileLibrary(source);

        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);

        var diagnostic = ir.Diagnostics.Single(d =>
            d.Code == DiagnosticCodes.InvalidExportsSubpathRoot
        );

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location).IsNotNull();
        await Assert.That(ir.PackageSubpathRoot).IsNull();
    }

    [Test]
    public async Task InvalidRoot_IsIgnoredByConsumers()
    {
        var output = TranspileConsumerOf(
            "VideoStudio.Domain.Episodes.Episode",
            subpathRoot: "VideoStudio..Domain"
        );

        await Assert
            .That(output)
            .Contains(
                "import type { Episode } from \"@video-studio/domain/video-studio/domain/episodes\""
            );
    }

    [Test]
    public async Task ValidRoot_ReachesTheIr()
    {
        var compilation = TranspileHelper.CompileLibrary(Library(Root));

        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);

        await Assert.That(ir.PackageSubpathRoot).IsEqualTo(Root);
        await Assert
            .That(ir.Diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidExportsSubpathRoot))
            .IsFalse();
    }

    [Test]
    public async Task ValidRoot_LeavesOnDiskLayoutAndInternalImportsUnchanged()
    {
        var files = TranspileHelper.Transpile(Library(Root));

        await Assert.That(files).ContainsKey("video-studio/domain/episodes/episode.ts");
        await Assert.That(files).ContainsKey("video-studio/domain/episodes/index.ts");
        await Assert
            .That(files["video-studio/domain/episodes/episode.ts"])
            .Contains("from \"#/video-studio/domain/clock\"");
    }

    private static string Library(string subpathRoot) =>
        $$"""
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@video-studio/domain", SubpathRoot = "{{subpathRoot}}")]

            namespace VideoStudio.Domain
            {
                public record Clock(int Ticks);
            }

            namespace VideoStudio.Domain.Episodes
            {
                public record Episode(string Title, VideoStudio.Domain.Clock StartedAt);
            }

            namespace VideoStudio.DomainEvents
            {
                public record Published(string Title);
            }
            """;

    private static string TranspileConsumerOf(string typeName, string subpathRoot = Root)
    {
        var consumer = $$"""
            [assembly: TranspileAssembly]

            namespace App;

            public class Holder
            {
                public {{typeName}}? Value { get; set; }
            }
            """;

        return TranspileHelper.TranspileWithLibrary(Library(subpathRoot), consumer)[
            "app/holder.ts"
        ];
    }

    private JsonObject WriteExports(string? subpathRoot, params string[] barrels)
    {
        var tempDir = CreateTempDir();

        PackageJsonWriter.UpdateOrCreate(
            tempDir,
            Path.Combine(tempDir, "src"),
            Barrels(barrels),
            subpathRoot: subpathRoot
        );

        return ReadExports(tempDir);
    }

    private static TsSourceFile[] Barrels(params string[] fileNames) =>
        [.. fileNames.Select(name => new TsSourceFile(name, [], ""))];

    private static string[] KeysOf(JsonObject exports) => [.. exports.Select(e => e.Key)];

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-subpath-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static JsonObject ReadExports(string dir) => (ReadJson(dir)["exports"] as JsonObject)!;

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;
}
