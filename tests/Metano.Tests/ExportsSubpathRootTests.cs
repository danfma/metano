using System.Text.Json.Nodes;
using Metano.Compiler;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Caching;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Tests;

/// <summary>
/// Spec 008 (#267): an opt-in <c>[EmitPackage(SubpathRoot = …)]</c> strips a namespace
/// prefix from <c>package.json#exports</c> keys and from every consumer's cross-package
/// specifier, while the on-disk layout and the <c>#/*</c> alias stay full-namespace.
/// </summary>
public class ExportsSubpathRootTests
{
    private const string Root = "VideoStudio.Domain";

    private const string FullEpisodesSubpath = "@video-studio/domain/video-studio/domain/episodes";

    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    // ── PackageSubpathRoot value object ────────────────────────────────────

    [Test]
    public async Task SubpathRoot_DefaultIsNone()
    {
        await Assert.That(default(PackageSubpathRoot)).IsEqualTo(PackageSubpathRoot.None);
        await Assert.That(PackageSubpathRoot.None.IsNone).IsTrue();
        await Assert.That(PackageSubpathRoot.None.Namespace).IsEqualTo("");
    }

    [Test]
    [Arguments("VideoStudio.Domain", true)]
    [Arguments("Acme", true)]
    [Arguments("Video Studio.Domain", false)]
    [Arguments("VideoStudio..Domain", false)]
    [Arguments(".VideoStudio", false)]
    [Arguments("global::VideoStudio", false)]
    public async Task SubpathRoot_TryFrom_AcceptsOnlyDottedNamespaces(string value, bool valid)
    {
        var parsed = PackageSubpathRoot.TryFrom(value, out var root);

        await Assert.That(parsed).IsEqualTo(valid);
        await Assert.That(root.IsNone).IsEqualTo(!valid);
    }

    [Test]
    public async Task SubpathRoot_TryFrom_EmptyIsNone()
    {
        var parsed = PackageSubpathRoot.TryFrom("", out var root);

        await Assert.That(parsed).IsTrue();
        await Assert.That(root).IsEqualTo(PackageSubpathRoot.None);
    }

    // ── package.json#exports ───────────────────────────────────────────────

    [Test]
    public async Task BarrelUnderRoot_ExportsStrippedKeyWithFullPathTargets()
    {
        var written = WriteExports(Root, "video-studio/domain/episodes/index.ts");

        var entry = written.Exports["./episodes"] as JsonObject;

        await Assert.That(written.Exports.ContainsKey("./video-studio/domain/episodes")).IsFalse();
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
        var written = WriteExports(Root, "video-studio/domain/index.ts");

        await Assert.That(KeysOf(written.Exports)).IsEquivalentTo(["."]);

        await Assert
            .That(written.Exports["."]?["import"]?.GetValue<string>())
            .IsEqualTo("./dist/video-studio/domain/index.js");
    }

    [Test]
    public async Task BarrelOutsideRoot_KeepsFullPath()
    {
        var written = WriteExports(Root, "shared/kernel/index.ts");

        await Assert.That(KeysOf(written.Exports)).IsEquivalentTo(["./shared/kernel"]);
    }

    [Test]
    public async Task SharedStringPrefixWithoutSegmentBoundary_KeepsFullPath()
    {
        var written = WriteExports(Root, "video-studio/domain-events/index.ts");

        await Assert.That(KeysOf(written.Exports)).IsEquivalentTo(["./video-studio/domain-events"]);
    }

    [Test]
    public async Task NoRoot_KeepsFullNamespaceKeys()
    {
        var written = WriteExports(
            null,
            "video-studio/domain/index.ts",
            "video-studio/domain/episodes/index.ts"
        );

        await Assert
            .That(KeysOf(written.Exports))
            .IsEquivalentTo(["./video-studio/domain", "./video-studio/domain/episodes"]);
    }

    [Test]
    public async Task OutputPrefix_IsPrependedAfterStripping()
    {
        var tempDir = CreateTempDir();

        var diagnostics = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            Path.Combine(tempDir, "src", "domain"),
            Barrels("video-studio/domain/index.ts", "video-studio/domain/episodes/index.ts"),
            srcRoot: "src",
            subpathRoot: RootOf(Root)
        );

        await Assert.That(diagnostics).IsEmpty();

        await Assert
            .That(KeysOf(ReadExports(tempDir)))
            .IsEquivalentTo(["./domain", "./domain/episodes"]);
    }

    [Test]
    public async Task Root_LeavesImportsAliasUnchanged()
    {
        var written = WriteExports(Root, "video-studio/domain/episodes/index.ts");

        var alias = written.Json["imports"]?["#/*"] as JsonObject;

        await Assert.That(written.Diagnostics).IsEmpty();
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
        PackageJsonWriter.UpdateOrCreate(tempDir, outputDir, files, subpathRoot: RootOf(Root));

        await Assert.That(KeysOf(ReadExports(tempDir))).IsEquivalentTo(["./episodes"]);
    }

    [Test]
    [Arguments("shared/index.ts", "video-studio/domain/shared/index.ts")]
    [Arguments("video-studio/domain/shared/index.ts", "shared/index.ts")]
    public async Task CollidingKey_WriterKeepsTheBarrelUnderTheRoot(string first, string second)
    {
        var written = WriteExports(Root, first, second);

        await Assert.That(written.Diagnostics).IsEmpty();

        await Assert
            .That(written.Exports["./shared"]?["import"]?.GetValue<string>())
            .IsEqualTo("./dist/video-studio/domain/shared/index.js");
    }

    [Test]
    public async Task Resolve_UnderRootBarrelWinsInEitherVisitOrder()
    {
        // Ordinal order visits "app/core/zeta/…" before "zeta/…" but "shared/…" before
        // "video-studio/…": the barrel under the root must win both ways.
        var claimedFirst = PackageExportSubpaths.Resolve(
            ["app/core/zeta/index.ts", "zeta/index.ts"],
            RootOf("App.Core")
        );

        var claimedSecond = PackageExportSubpaths.Resolve(
            ["shared/index.ts", "video-studio/domain/shared/index.ts"],
            RootOf(Root)
        );

        await Assert
            .That(claimedFirst.Collisions.Single().Kept.Directory)
            .IsEqualTo("app/core/zeta");

        await Assert.That(claimedFirst.Collisions.Single().Dropped.Directory).IsEqualTo("zeta");

        await Assert
            .That(claimedSecond.Collisions.Single().Kept.Directory)
            .IsEqualTo("video-studio/domain/shared");
    }

    // ── Transform-time collision diagnostics (MS0030) ───────────────────────

    [Test]
    public async Task CollidingNamespaces_ReportMs0030FromTheTransform()
    {
        var source = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@video-studio/domain", SubpathRoot = "VideoStudio.Domain")]

            namespace VideoStudio.Domain.Shared
            {
                public record Tag(string Name);
            }

            namespace Shared
            {
                public record Label(string Text);
            }
            """;

        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        var collision = diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ExportsSubpathKeyCollision
        );

        await Assert.That(collision.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(collision.Message).Contains("'./shared'");
        await Assert.That(collision.Message).Contains("SubpathRoot = \"VideoStudio.Domain\"");
    }

    [Test]
    public async Task NamespaceBarrelsWithRootNamespaceTypes_CollideOnPackageRoot()
    {
        var compilation = TranspileHelper.CompileLibrary(Library(Root));
        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);
        var transformer = new TypeTransformer(ir, compilation) { NamespaceBarrels = true };

        transformer.TransformAll();

        var collision = transformer.Diagnostics.Single(d =>
            d.Code == DiagnosticCodes.ExportsSubpathKeyCollision
        );

        await Assert.That(collision.Message).Contains("'.'");
        await Assert.That(collision.Message).Contains("<output root>");
    }

    [Test]
    public async Task Collision_StopsTheGroupCacheFromBeingWritten()
    {
        var cacheDir = CreateTempDir();
        var compilation = TranspileHelper.CompileLibrary(Library(Root));
        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);

        var transformer = new TypeTransformer(ir, compilation)
        {
            NamespaceBarrels = true,
            CacheOutputDir = cacheDir,
            CacheConfigurationFingerprint = "fp",
        };

        transformer.TransformAll();

        await Assert
            .That(
                transformer.Diagnostics.Any(d =>
                    d.Code == DiagnosticCodes.ExportsSubpathKeyCollision
                )
            )
            .IsTrue();

        await Assert.That(GroupCacheFile.TryRead(cacheDir)).IsNull();
    }

    [Test]
    public async Task NoRoot_NoCollisionDiagnostics()
    {
        var (_, diagnostics) = TranspileHelper.TranspileWithDiagnostics(
            Library(Root).Replace(", SubpathRoot = \"VideoStudio.Domain\"", "")
        );

        await Assert
            .That(diagnostics.Any(d => d.Code == DiagnosticCodes.ExportsSubpathKeyCollision))
            .IsFalse();
    }

    // ── Cross-package consumers ────────────────────────────────────────────

    [Test]
    public async Task ConsumerImport_UsesStrippedSubpath()
    {
        var output = TranspileConsumerOf("VideoStudio.Domain.Episodes.Episode");

        await Assert
            .That(output)
            .Contains("import type { Episode } from \"@video-studio/domain/episodes\"");

        await Assert.That(output).DoesNotContain(FullEpisodesSubpath);
    }

    [Test]
    public async Task ConsumerImport_OfRootNamespaceType_UsesBarePackage()
    {
        var output = TranspileConsumerOf("VideoStudio.Domain.Clock");

        await Assert.That(output).Contains("import type { Clock } from \"@video-studio/domain\"");
        await Assert.That(output).DoesNotContain("@video-studio/domain/video-studio");
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
    public async Task ConsumerImports_FromRootAndChild_MergePerSubpath()
    {
        var consumer = """
            [assembly: TranspileAssembly]

            namespace App;

            public class Timeline
            {
                public VideoStudio.Domain.Clock? Clock { get; set; }
                public VideoStudio.Domain.Timecode? Start { get; set; }
                public VideoStudio.Domain.Episodes.Episode? Episode { get; set; }
            }
            """;

        var output = TranspileHelper.TranspileWithLibrary(Library(Root), consumer)[
            "app/timeline.ts"
        ];

        await Assert
            .That(output)
            .Contains("import type { Clock, Timecode } from \"@video-studio/domain\"");

        await Assert
            .That(output)
            .Contains("import type { Episode } from \"@video-studio/domain/episodes\"");

        await Assert.That(output).DoesNotContain("@video-studio/domain/video-studio");
    }

    [Test]
    public async Task ConsumerImport_OfEnum_UsesStrippedSubpath()
    {
        var output = TranspileConsumerOf("VideoStudio.Domain.Episodes.EpisodeStatus");

        await Assert
            .That(output)
            .Contains("EpisodeStatus } from \"@video-studio/domain/episodes\"");
        await Assert.That(output).DoesNotContain(FullEpisodesSubpath);
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
        await Assert.That(output).DoesNotContain("@video-studio/domain/video-studio");
    }

    [Test]
    public async Task ConsumerJsxComponent_UsesStrippedSubpath()
    {
        var library = """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;
            using Metano.TypeScript.SolidJs;
            using Metano.TypeScript.SolidJs.Web;
            using Metano.TypeScript.DOM;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@video-studio/ui", SubpathRoot = "VideoStudio.Ui")]

            namespace VideoStudio.Ui.Badges;

            public sealed record Badge : JsxComponent
            {
                public override JsxElement Render() => new Html.Span { Children = [Text("b")] };
            }
            """;

        var consumer = """
            using Metano.Annotations;
            using Metano.Annotations.TypeScript;
            using Metano.TypeScript.SolidJs;
            using Metano.TypeScript.SolidJs.Web;
            using Metano.TypeScript.DOM;
            using VideoStudio.Ui.Badges;

            [Transpile]
            public sealed record Host : JsxComponent
            {
                public override JsxElement Render() => new Html.Div { Children = [new Badge()] };
            }
            """;

        var tsx = TranspileHelper.TranspileJsxWithLibrary(library, consumer)["host.tsx"];

        await Assert.That(tsx).Contains("from \"@video-studio/ui/badges\"");
        await Assert.That(tsx).DoesNotContain("@video-studio/ui/video-studio");
    }

    // ── Invalid roots and other targets ────────────────────────────────────

    [Test]
    public async Task InvalidRoot_ReportsMs0029AtTheAttribute()
    {
        var compilation = TranspileHelper.CompileLibrary(Library("Video Studio..Domain"));

        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);

        var diagnostic = ir.Diagnostics.Single(d =>
            d.Code == DiagnosticCodes.InvalidExportsSubpathRoot
        );

        await Assert.That(diagnostic.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location).IsNotNull();
        await Assert.That(diagnostic.Message).DoesNotContain("is ignored");
        await Assert.That(ir.PackageSubpathRoot).IsEqualTo(PackageSubpathRoot.None);
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
            .Contains($"import type {{ Episode }} from \"{FullEpisodesSubpath}\"");
    }

    [Test]
    public async Task DartTarget_IgnoresTheRoot()
    {
        var source = """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("video_studio", EmitTarget.Dart, SubpathRoot = "Video Studio")]

            namespace VideoStudio.Domain;

            public record Clock(int Ticks);
            """;

        var compilation = TranspileHelper.CompileLibrary(source);

        var ir = new CSharpSourceFrontend().ExtractFromCompilation(
            compilation,
            Metano.Annotations.TargetLanguage.Dart
        );

        await Assert
            .That(ir.Diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidExportsSubpathRoot))
            .IsFalse();
    }

    [Test]
    public async Task ValidRoot_ReachesTheIr()
    {
        var compilation = TranspileHelper.CompileLibrary(Library(Root));

        var (ir, _) = TranspileHelper.NewTransformerWithIr(compilation);

        await Assert.That(ir.PackageSubpathRoot).IsEqualTo(RootOf(Root));

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

    // ── Per-group cache ────────────────────────────────────────────────────

    [Test]
    public async Task GroupCache_ReusedWhileReferencedRootIsUnchanged()
    {
        var cacheDir = CreateTempDir();

        TransformConsumerWithCache(cacheDir, Root);

        var second = TransformConsumerWithCache(cacheDir, Root);

        await Assert.That(second.HolderReusedFromCache).IsTrue();
    }

    [Test]
    public async Task GroupCache_InvalidatedWhenReferencedRootChanges()
    {
        var cacheDir = CreateTempDir();

        TransformConsumerWithCache(cacheDir, "VideoStudio");

        var second = TransformConsumerWithCache(cacheDir, Root);

        await Assert.That(second.HolderReusedFromCache).IsFalse();
        await Assert.That(second.Holder).Contains("from \"@video-studio/domain/episodes\"");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private WrittenPackage WriteExports(string? subpathRoot, params string[] barrels)
    {
        var tempDir = CreateTempDir();

        var diagnostics = PackageJsonWriter.UpdateOrCreate(
            tempDir,
            Path.Combine(tempDir, "src"),
            Barrels(barrels),
            subpathRoot: RootOf(subpathRoot)
        );

        return new WrittenPackage(ReadJson(tempDir), diagnostics);
    }

    /// <summary>
    /// Transforms a consumer of <see cref="Library"/> with the per-group cache enabled
    /// in <paramref name="cacheDir"/>, then writes the printed files there the way the
    /// host would, so the next run can reuse them. Reports whether the consumer's
    /// group was reused and what its file now holds.
    /// </summary>
    private static CachedRun TransformConsumerWithCache(string cacheDir, string subpathRoot)
    {
        var library = TranspileHelper.CompileLibrary(Library(subpathRoot));

        var consumer = TranspileHelper.CompileConsumer(
            Consumer("VideoStudio.Domain.Episodes.Episode"),
            library
        );

        var (ir, _) = TranspileHelper.NewTransformerWithIr(consumer);

        var transformer = new TypeTransformer(ir, consumer)
        {
            CacheOutputDir = cacheDir,
            CacheConfigurationFingerprint = "fp",
        };

        var printer = new Printer();

        var files = transformer.TransformAll();

        foreach (var file in files)
        {
            var path = Path.Combine(cacheDir, file.FileName);

            var content = transformer.CachedFileContents.TryGetValue(file.FileName, out var cached)
                ? cached
                : printer.Print(file);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        // A reused group surfaces as a metadata stub: its class has no members.
        var holder = files.Single(f => f.FileName == "app/holder.ts");
        var reused = holder.Statements.OfType<TsClass>().Single().Members.Count == 0;

        return new CachedRun(reused, File.ReadAllText(Path.Combine(cacheDir, "app", "holder.ts")));
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"metano-subpath-root-{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        return dir;
    }

    private static string Library(string subpathRoot) =>
        $$"""
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@video-studio/domain", SubpathRoot = "{{subpathRoot}}")]

            namespace VideoStudio.Domain
            {
                public record Clock(int Ticks);

                public record Timecode(int Frames);
            }

            namespace VideoStudio.Domain.Episodes
            {
                public record Episode(string Title, VideoStudio.Domain.Clock StartedAt);

                public enum EpisodeStatus { Draft, Published }
            }

            namespace VideoStudio.DomainEvents
            {
                public record Published(string Title);
            }
            """;

    private static string Consumer(string typeName) =>
        $$"""
            [assembly: TranspileAssembly]

            namespace App;

            public class Holder
            {
                public {{typeName}}? Value { get; set; }
            }
            """;

    private static string TranspileConsumerOf(string typeName, string subpathRoot = Root) =>
        TranspileHelper.TranspileWithLibrary(Library(subpathRoot), Consumer(typeName))[
            "app/holder.ts"
        ];

    private static PackageSubpathRoot RootOf(string? dottedNamespace)
    {
        PackageSubpathRoot.TryFrom(dottedNamespace, out var root);

        return root;
    }

    private static TsSourceFile[] Barrels(params string[] fileNames) =>
        [.. fileNames.Select(name => new TsSourceFile(name, [], ""))];

    private static string[] KeysOf(JsonObject exports) => [.. exports.Select(e => e.Key)];

    private static JsonObject ReadExports(string dir) => (ReadJson(dir)["exports"] as JsonObject)!;

    private static JsonObject ReadJson(string dir) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json"))) as JsonObject)!;

    private sealed record CachedRun(bool HolderReusedFromCache, string Holder);

    private sealed record WrittenPackage(
        JsonObject Json,
        IReadOnlyList<MetanoDiagnostic> Diagnostics
    )
    {
        public JsonObject Exports => (Json["exports"] as JsonObject)!;
    }
}
