using System.Text.Json.Nodes;
using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests.Caching;

/// <summary>
/// Issue #273: a run served from the incremental cache must leave <c>package.json</c>
/// exactly as the cold run wrote it. Each test drives the real CLI pipeline twice —
/// <see cref="TranspilerHost"/> followed by <see cref="PackageJsonReconciler"/>, with a
/// fresh <see cref="TypeScriptTarget"/> per run as in separate CLI invocations — into the
/// same output directory, then compares the two manifests byte for byte.
/// </summary>
public class PackageJsonCacheReuseTests
{
    private const string ItemsSource = """
        using Metano.Annotations;

        [assembly: TranspileAssembly]
        [assembly: EmitPackage("@acme/todo")]

        namespace Acme.Todo.Items
        {
            public record TodoItem(string Title)
            {
                public string Describe() => Title;
            }
        }
        """;

    private const string ListsSource = """
        namespace Acme.Todo.Lists
        {
            public class TodoList
            {
                public int Count() => 0;
            }
        }
        """;

    private static readonly string ItemsSourceWithSubpathRoot = ItemsSource.Replace(
        "[assembly: EmitPackage(\"@acme/todo\")]",
        "[assembly: EmitPackage(\"@acme/todo\", SubpathRoot = \"Acme.Todo\")]"
    );

    private readonly List<string> _tempDirs = [];

    [After(Test)]
    public void DeleteTempDirs()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task FullCacheHit_KeepsPackageJsonIdentical()
    {
        var package = NewPackage();
        var sources = new[] { ItemsSource, ListsSource };

        var coldManifest = await TranspileColdAndReadManifest(package, sources);
        var reused = await TranspileAndReconcile(package, sources);
        var reusedManifest = ReadManifest(package);

        await Assert.That(reused.ServedFromCache).IsTrue();
        await Assert.That(reusedManifest).IsEqualTo(coldManifest);

        await Assert
            .That(ExportKeys(reusedManifest))
            .IsEquivalentTo(["./acme/todo/items", "./acme/todo/lists"]);
    }

    [Test]
    public async Task FullCacheHit_WithSubpathRoot_KeepsStrippedExportKeys()
    {
        var package = NewPackage();
        var sources = new[] { ItemsSourceWithSubpathRoot, ListsSource };

        var coldManifest = await TranspileColdAndReadManifest(package, sources);
        var reused = await TranspileAndReconcile(package, sources);
        var reusedManifest = ReadManifest(package);

        await Assert.That(reused.ServedFromCache).IsTrue();
        await Assert.That(reusedManifest).IsEqualTo(coldManifest);
        await Assert.That(ExportKeys(reusedManifest)).IsEquivalentTo(["./items", "./lists"]);
    }

    [Test]
    public async Task PartialCacheHit_KeepsPackageJsonIdentical()
    {
        var package = NewPackage();

        var coldManifest = await TranspileColdAndReadManifest(package, [ItemsSource, ListsSource]);
        var editedLists = ListsSource.Replace("Count() => 0", "Count() => 1");
        var partial = await TranspileAndReconcile(package, [ItemsSource, editedLists]);
        var partialManifest = ReadManifest(package);

        await Assert.That(partial.ServedFromCache).IsFalse();
        await Assert.That(partialManifest).IsEqualTo(coldManifest);
    }

    [Test]
    public async Task FullCacheHit_KeepsCrossPackageDependencies()
    {
        var package = NewPackage();

        var library = TranspileHelper.CompileLibrary(
            """
            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@acme/shared", Version = "1.2.3")]

            namespace Acme.Shared
            {
                public record Tag(string Name);
            }
            """
        );

        var consumerSource = """
            using Metano.Annotations;
            using Acme.Shared;

            [assembly: TranspileAssembly]
            [assembly: EmitPackage("@acme/todo")]

            namespace Acme.Todo.Items
            {
                public record TodoItem(string Title, Tag Tag);
            }
            """;

        var coldManifest = await TranspileColdAndReadManifest(package, [consumerSource], library);
        var reused = await TranspileAndReconcile(package, [consumerSource], library);
        var reusedManifest = ReadManifest(package);

        await Assert.That(reused.ServedFromCache).IsTrue();
        await Assert.That(reusedManifest).IsEqualTo(coldManifest);

        await Assert
            .That(JsonNode.Parse(reusedManifest)!["dependencies"]?["@acme/shared"]?.ToString())
            .IsEqualTo("1.2.3");
    }

    /// <summary>
    /// Runs the baseline pass and checks it really transformed, so a leftover cache can
    /// never turn the cold manifest into a cache-hit one.
    /// </summary>
    private static async Task<string> TranspileColdAndReadManifest(
        PackageLayout package,
        IReadOnlyList<string> sources,
        Compilation? library = null
    )
    {
        var cold = await TranspileAndReconcile(package, sources, library);

        if (cold.ServedFromCache)
            throw new InvalidOperationException("The cold run was unexpectedly served from cache.");

        return ReadManifest(package);
    }

    private static async Task<PipelineRun> TranspileAndReconcile(
        PackageLayout package,
        IReadOnlyList<string> sources,
        Compilation? library = null
    )
    {
        var target = new TypeScriptTarget();
        var recordingTarget = new CacheAdoptionRecorder(target);
        var compilation = Compile(package, sources, library);
        var options = new TranspileOptions(package.ProjectPath, package.OutputDir);

        var result = await TranspilerHost.RunAsync(
            options,
            recordingTarget,
            new InMemorySourceFrontend(compilation)
        );

        if (!result.Success)
            throw new InvalidOperationException(
                $"Transpilation failed: {result.ErrorCount} error(s)."
            );

        var diagnostics = PackageJsonReconciler.Reconcile(
            target,
            package.Root,
            package.OutputDir,
            dist: "./dist",
            srcRoot: null,
            importAlias: null
        );

        if (diagnostics is null || diagnostics.Count > 0)
            throw new InvalidOperationException("package.json was not reconciled cleanly.");

        return new PipelineRun(recordingTarget.AdoptedCachedOutput);
    }

    /// <summary>
    /// Each source gets its own on-disk-looking path: the cache keys source hashes by
    /// syntax-tree path and skips path-less trees, so a path is what makes an edit
    /// invalidate the full-run cache.
    /// </summary>
    private static CSharpCompilation Compile(
        PackageLayout package,
        IReadOnlyList<string> sources,
        Compilation? library
    )
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);

        var trees = sources.Select(
            (source, index) =>
                CSharpSyntaxTree.ParseText(
                    source,
                    parseOptions,
                    path: Path.Combine(package.Root, $"Source{index}.cs")
                )
        );

        var references = library is null
            ? TranspileHelper.BaseReferences
            : [.. TranspileHelper.BaseReferences, library.ToMetadataReference()];

        return CSharpCompilation.Create(
            "TodoPackage",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    private static string ReadManifest(PackageLayout package) =>
        File.ReadAllText(Path.Combine(package.Root, "package.json"));

    private static IReadOnlyList<string> ExportKeys(string manifest) =>
        JsonNode.Parse(manifest)!["exports"]!.AsObject().Select(entry => entry.Key).ToList();

    private PackageLayout NewPackage()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "metano-pkg-cache-test-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(root);
        _tempDirs.Add(root);

        return new PackageLayout(root);
    }

    private sealed record PackageLayout(string Root)
    {
        public string OutputDir => Path.Combine(Root, "src");

        public string ProjectPath => Path.Combine(Root, "TodoPackage.csproj");
    }

    private sealed record PipelineRun(bool ServedFromCache);

    /// <summary>
    /// Forwards to the real target and records whether the host took the full
    /// cache-hit path (<see cref="ITranspilerTarget.AdoptCachedOutput"/>) instead of
    /// <see cref="ITranspilerTarget.Transform"/>.
    /// </summary>
    private sealed class CacheAdoptionRecorder(TypeScriptTarget inner) : ITranspilerTarget
    {
        public bool AdoptedCachedOutput { get; private set; }

        public string Name => inner.Name;

        public TargetLanguage Language => inner.Language;

        public string ConfigurationFingerprint => inner.ConfigurationFingerprint;

        public TargetOutput Transform(
            IrCompilation ir,
            Compilation? compilation,
            string? outputDir = null,
            string? filePrefix = null
        ) => inner.Transform(ir, compilation, outputDir, filePrefix);

        public void AdoptCachedOutput(
            IrCompilation ir,
            Compilation? compilation,
            IReadOnlyList<GeneratedFile> files
        )
        {
            AdoptedCachedOutput = true;
            inner.AdoptCachedOutput(ir, compilation, files);
        }
    }

    /// <summary>
    /// Serves a prebuilt in-memory compilation through the production C# extraction, so
    /// the host runs its real cache and emit path without loading an MSBuild project.
    /// </summary>
    private sealed class InMemorySourceFrontend(Compilation compilation) : ISourceFrontend
    {
        private readonly CSharpSourceFrontend _extractor = new();

        public string Name => "in-memory";

        public Compilation? LoadedCompilation => compilation;

        public int LoadErrorCount => 0;

        public Task<IrCompilation> ExtractAsync(
            string projectPath,
            CancellationToken ct = default,
            TargetLanguage target = TargetLanguage.TypeScript
        ) => Task.FromResult(_extractor.ExtractFromCompilation(compilation, target));
    }
}
