using System.Text.Json.Nodes;
using Metano.Annotations;
using Metano.Compiler;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Metano.Tests.Caching;

/// <summary>
/// Issues #273 and #299: a run served from the incremental cache must leave
/// <c>package.json</c> exactly as the cold run wrote it, even when it was deleted in
/// between. Each test drives the real CLI pipeline twice —
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

    private const string SharedLibrarySource = """
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("@acme/shared", Version = "1.2.3")]

        namespace Acme.Shared
        {
            public record Tag(string Name);
        }
        """;

    private const string TaggedItemsSource = """
        using Metano.Annotations;
        using Acme.Shared;

        [assembly: TranspileAssembly]
        [assembly: EmitPackage("@acme/todo")]

        namespace Acme.Todo.Items
        {
            public record TodoItem(string Title, Tag Tag);
        }
        """;

    private const string DecimalListsSource = """
        namespace Acme.Todo.Lists
        {
            public class TodoList
            {
                public decimal Total() => 0m;
            }
        }
        """;

    private const string EditedDecimalListsSource = """
        namespace Acme.Todo.Lists
        {
            public class TodoList
            {
                public decimal Total() => 1m;
            }
        }
        """;

    private static readonly string BumpedSharedLibrarySource = SharedLibrarySource.Replace(
        "Version = \"1.2.3\"",
        "Version = \"2.0.0\""
    );

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
        var library = TranspileHelper.CompileLibrary(SharedLibrarySource);

        var coldManifest = await TranspileColdAndReadManifest(
            package,
            [TaggedItemsSource],
            library
        );

        var reused = await TranspileAndReconcile(package, [TaggedItemsSource], library);
        var reusedManifest = ReadManifest(package);

        await Assert.That(reused.ServedFromCache).IsTrue();
        await Assert.That(reusedManifest).IsEqualTo(coldManifest);
        await Assert.That(DependencyVersion(reusedManifest, "@acme/shared")).IsEqualTo("1.2.3");
    }

    /// <summary>
    /// Issue #299: the full-hit path never transforms, so the cross-package dependencies
    /// must come back from the cache for a package.json deleted after the cold run.
    /// </summary>
    [Test]
    public async Task FullCacheHit_RebuildsDeletedPackageJsonWithDependencies()
    {
        var package = NewPackage();
        var library = TranspileHelper.CompileLibrary(SharedLibrarySource);
        var sources = new[] { TaggedItemsSource, DecimalListsSource };
        var coldManifest = await TranspileColdAndReadManifest(package, sources, library);

        DeleteManifest(package);
        var reused = await TranspileAndReconcile(package, sources, library);
        var rebuiltManifest = ReadManifest(package);

        await Assert.That(reused.ServedFromCache).IsTrue();
        await Assert.That(rebuiltManifest).IsEqualTo(coldManifest);

        await Assert
            .That(DependencyNames(rebuiltManifest))
            .IsEquivalentTo(["@acme/shared", "decimal.js"]);
    }

    /// <summary>
    /// Issue #299: a partial hit reuses unchanged groups from the per-group cache, so the
    /// dependencies only those groups require must come back from that cache.
    /// </summary>
    [Test]
    public async Task PartialCacheHit_RebuildsDeletedPackageJsonWithReusedGroupDependencies()
    {
        var package = NewPackage();
        var library = TranspileHelper.CompileLibrary(SharedLibrarySource);
        var editedSources = new[] { TaggedItemsSource, EditedDecimalListsSource };

        await TranspileColdAndReadManifest(
            package,
            [TaggedItemsSource, DecimalListsSource],
            library
        );

        var coldEditedManifest = await TranspileColdInFreshPackage(editedSources, library);

        DeleteManifest(package);
        var partial = await TranspileAndReconcile(package, editedSources, library);
        var rebuiltManifest = ReadManifest(package);

        await Assert.That(partial.ServedFromCache).IsFalse();
        await Assert.That(rebuiltManifest).IsEqualTo(coldEditedManifest);

        await Assert
            .That(DependencyNames(rebuiltManifest))
            .IsEquivalentTo(["@acme/shared", "decimal.js"]);
    }

    /// <summary>
    /// A dependency dropped by a re-transformed group must not survive from the cache:
    /// the rebuilt manifest matches a cold run of the edited sources.
    /// </summary>
    [Test]
    public async Task PartialCacheHit_DropsDependencyOfReTransformedGroup()
    {
        var package = NewPackage();
        var library = TranspileHelper.CompileLibrary(SharedLibrarySource);
        var listsWithoutDecimal = DecimalListsSource.Replace(
            "decimal Total() => 0m",
            "int Total() => 0"
        );

        var editedSources = new[] { TaggedItemsSource, listsWithoutDecimal };
        await TranspileColdAndReadManifest(
            package,
            [TaggedItemsSource, DecimalListsSource],
            library
        );
        var coldEditedManifest = await TranspileColdInFreshPackage(editedSources, library);

        DeleteManifest(package);
        var partial = await TranspileAndReconcile(package, editedSources, library);
        var rebuiltManifest = ReadManifest(package);

        await Assert.That(partial.ServedFromCache).IsFalse();
        await Assert.That(rebuiltManifest).IsEqualTo(coldEditedManifest);
        await Assert.That(DependencyNames(rebuiltManifest)).IsEquivalentTo(["@acme/shared"]);
    }

    /// <summary>
    /// A producer that bumps its <c>[EmitPackage(Version)]</c> must not leave a reused
    /// group restoring the old version into a rebuilt package.json.
    /// </summary>
    [Test]
    public async Task PartialCacheHit_RebuildsDeletedPackageJsonWithBumpedLibraryVersion()
    {
        var package = NewPackage();
        var libraryV1 = TranspileHelper.CompileLibrary(SharedLibrarySource);
        var libraryV2 = TranspileHelper.CompileLibrary(BumpedSharedLibrarySource);
        var editedSources = new[] { TaggedItemsSource, EditedDecimalListsSource };

        await TranspileColdAndReadManifest(
            package,
            [TaggedItemsSource, DecimalListsSource],
            libraryV1
        );

        var coldBumpedManifest = await TranspileColdInFreshPackage(editedSources, libraryV2);

        DeleteManifest(package);
        var partial = await TranspileAndReconcile(package, editedSources, libraryV2);
        var rebuiltManifest = ReadManifest(package);

        await Assert.That(partial.ServedFromCache).IsFalse();
        await Assert.That(rebuiltManifest).IsEqualTo(coldBumpedManifest);
        await Assert.That(DependencyVersion(rebuiltManifest, "@acme/shared")).IsEqualTo("2.0.0");
    }

    /// <summary>
    /// Several groups share the bumped package and only one is edited: every group must
    /// agree on the new version, whichever worker thread records it first.
    /// </summary>
    [Test]
    public async Task PartialCacheHit_UpdatesSharedDependencyVersionAfterLibraryBump()
    {
        var package = NewPackage();
        var libraryV1 = TranspileHelper.CompileLibrary(SharedLibrarySource);
        var libraryV2 = TranspileHelper.CompileLibrary(BumpedSharedLibrarySource);
        var coldSources = TagUserSources(editedIndex: null);
        var editedSources = TagUserSources(editedIndex: 3);

        await TranspileColdAndReadManifest(package, coldSources, libraryV1);
        var coldBumpedManifest = await TranspileColdInFreshPackage(editedSources, libraryV2);

        var partial = await TranspileAndReconcile(package, editedSources, libraryV2);
        var updatedManifest = ReadManifest(package);

        await Assert.That(partial.ServedFromCache).IsFalse();
        await Assert.That(updatedManifest).IsEqualTo(coldBumpedManifest);
        await Assert.That(DependencyVersion(updatedManifest, "@acme/shared")).IsEqualTo("2.0.0");
    }

    /// <summary>
    /// One file group per source, each referencing <c>Tag</c> from the shared library;
    /// the source at <paramref name="editedIndex"/> gets a body change.
    /// </summary>
    private static IReadOnlyList<string> TagUserSources(int? editedIndex)
    {
        const int GroupCount = 7;

        return Enumerable
            .Range(0, GroupCount)
            .Select(index =>
            {
                var label = index == editedIndex ? "edited" : "original";
                var assemblyAttributes =
                    index == 0
                        ? "[assembly: TranspileAssembly]\n[assembly: EmitPackage(\"@acme/todo\")]"
                        : "";

                return $$"""
                using Metano.Annotations;
                using Acme.Shared;

                {{assemblyAttributes}}

                namespace Acme.Todo.Group{{index}}
                {
                    public record Item{{index}}(Tag Tag)
                    {
                        public string Describe() => "{{label}}";
                    }
                }
                """;
            })
            .ToList();
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

    /// <summary>
    /// The cold-run manifest of <paramref name="sources"/> in a package of its own, the
    /// reference a cache-served run of the same sources must reproduce.
    /// </summary>
    private async Task<string> TranspileColdInFreshPackage(
        IReadOnlyList<string> sources,
        Compilation library
    ) => await TranspileColdAndReadManifest(NewPackage(), sources, library);

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

        var reconciliation = PackageJsonReconciler.Reconcile(
            target.LastPackageManifestInputs,
            package.Root,
            package.OutputDir,
            dist: "./dist",
            srcRoot: null,
            importAlias: null
        );

        if (!reconciliation.WasWritten || reconciliation.Diagnostics.Count > 0)
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

    private static void DeleteManifest(PackageLayout package) =>
        File.Delete(Path.Combine(package.Root, "package.json"));

    private static IReadOnlyList<string> DependencyNames(string manifest) =>
        JsonNode.Parse(manifest)!["dependencies"]!.AsObject().Select(entry => entry.Key).ToList();

    private static string? DependencyVersion(string manifest, string packageName) =>
        JsonNode.Parse(manifest)!["dependencies"]?[packageName]?.ToString();

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
            IReadOnlyList<GeneratedFile> files,
            IReadOnlyDictionary<string, string> packageDependencies
        )
        {
            AdoptedCachedOutput = true;
            inner.AdoptCachedOutput(ir, compilation, files, packageDependencies);
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
