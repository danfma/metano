using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Caching;

namespace Metano.Tests.Caching;

/// <summary>
/// Pin <see cref="FileMetadataExtractor"/>'s round-trip: extracting
/// metadata from a fresh <c>TsSourceFile</c> and rebuilding a stub
/// from it must preserve the exact shape <c>BarrelFileGenerator</c>
/// and <c>CyclicReferenceDetector</c> read.
/// </summary>
public class FileMetadataExtractorTests
{
    [Test]
    public async Task Extract_CapturesImportsAndExports()
    {
        var source = new TsSourceFile(
            "foo/bar.ts",
            [
                new TsImport(["UserId"], "#/shared-kernel/user-id"),
                new TsClass("Bar", null, [], Exported: true),
                new TsInterface("BarOptions", [], Exported: true),
            ]
        );

        var meta = FileMetadataExtractor.Extract(source, "content-placeholder");

        await Assert.That(meta.Path).IsEqualTo("foo/bar.ts");
        await Assert.That(meta.Imports.Count).IsEqualTo(1);
        await Assert.That(meta.Imports[0].From).IsEqualTo("#/shared-kernel/user-id");
        await Assert.That(meta.Imports[0].Names[0]).IsEqualTo("UserId");
        await Assert.That(meta.Exports.Count).IsEqualTo(2);
        await Assert
            .That(meta.Exports.Any(e => e.Name == "Bar" && e.Kind == CachedExportKind.Class))
            .IsTrue();
        await Assert
            .That(
                meta.Exports.Any(e =>
                    e.Name == "BarOptions" && e.Kind == CachedExportKind.Interface
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Extract_PreservesExportKindPerShape()
    {
        var source = new TsSourceFile(
            "foo/bar.ts",
            [
                new TsClass("CClass", null, [], Exported: true),
                new TsFunction("fFunc", [], new TsVoidType(), [], Exported: true),
                new TsEnum("EEnum", [], Exported: true),
                new TsConstObject("CoConst", [], Exported: true),
                new TsNamespaceDeclaration("NNamespace", [], Exported: true),
                new TsTypeAlias("TAlias", new TsAnyType(), Exported: true),
                new TsInterface("IIface", [], Exported: true),
            ]
        );

        var meta = FileMetadataExtractor.Extract(source, "content-placeholder");

        await Assert
            .That(meta.Exports.Single(e => e.Name == "CClass").Kind)
            .IsEqualTo(CachedExportKind.Class);
        await Assert
            .That(meta.Exports.Single(e => e.Name == "fFunc").Kind)
            .IsEqualTo(CachedExportKind.Function);
        await Assert
            .That(meta.Exports.Single(e => e.Name == "EEnum").Kind)
            .IsEqualTo(CachedExportKind.Enum);
        await Assert
            .That(meta.Exports.Single(e => e.Name == "CoConst").Kind)
            .IsEqualTo(CachedExportKind.ConstObject);
        await Assert
            .That(meta.Exports.Single(e => e.Name == "NNamespace").Kind)
            .IsEqualTo(CachedExportKind.Namespace);
        await Assert
            .That(meta.Exports.Single(e => e.Name == "TAlias").Kind)
            .IsEqualTo(CachedExportKind.TypeAlias);
        await Assert
            .That(meta.Exports.Single(e => e.Name == "IIface").Kind)
            .IsEqualTo(CachedExportKind.Interface);
    }

    [Test]
    public async Task Extract_AcknowledgesSideEffectShapes_WithoutThrowing()
    {
        var source = new TsSourceFile(
            "foo/bar.ts",
            [
                new TsReExport(["Foo"], "#/foo"),
                new TsModuleExport("Bar", IsDefault: false),
                new TsExportImportAlias("Alias", "Target"),
            ]
        );

        var meta = FileMetadataExtractor.Extract(source, "content-placeholder");

        // Side-effect shapes ride along in the cached on-disk content
        // and contribute nothing to the metadata's export surface.
        await Assert.That(meta.Exports.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Stub_Roundtrip_RebuildsConcreteAstShape()
    {
        var meta = new CachedFileMetadata(
            "foo/bar.ts",
            "deadbeef",
            [new CachedImport(["UserId"], "#/shared-kernel/user-id")],
            [
                new CachedExport("CClass", CachedExportKind.Class),
                new CachedExport("fFunc", CachedExportKind.Function),
                new CachedExport("EEnum", CachedExportKind.Enum),
                new CachedExport("CoConst", CachedExportKind.ConstObject),
                new CachedExport("NNamespace", CachedExportKind.Namespace),
                new CachedExport("TAlias", CachedExportKind.TypeAlias),
                new CachedExport("IIface", CachedExportKind.Interface),
            ]
        );

        var stub = FileMetadataExtractor.BuildStub(meta);

        await Assert.That(stub.FileName).IsEqualTo("foo/bar.ts");
        await Assert.That(stub.Statements.OfType<TsImport>().Count()).IsEqualTo(1);
        await Assert.That(stub.Statements.OfType<TsClass>().Any(c => c.Name == "CClass")).IsTrue();
        await Assert
            .That(stub.Statements.OfType<TsFunction>().Any(f => f.Name == "fFunc"))
            .IsTrue();
        await Assert.That(stub.Statements.OfType<TsEnum>().Any(e => e.Name == "EEnum")).IsTrue();
        await Assert
            .That(stub.Statements.OfType<TsConstObject>().Any(co => co.Name == "CoConst"))
            .IsTrue();
        await Assert
            .That(
                stub.Statements.OfType<TsNamespaceDeclaration>().Any(ns => ns.Name == "NNamespace")
            )
            .IsTrue();
        await Assert
            .That(stub.Statements.OfType<TsTypeAlias>().Any(ta => ta.Name == "TAlias"))
            .IsTrue();
        await Assert
            .That(stub.Statements.OfType<TsInterface>().Any(i => i.Name == "IIface"))
            .IsTrue();
    }

    [Test]
    public async Task NonExportedStatements_AreNotCaptured()
    {
        var source = new TsSourceFile(
            "foo/bar.ts",
            [
                new TsImport(["Helper"], "#/util/helper"),
                new TsClass("Internal", null, [], Exported: false),
            ]
        );

        var meta = FileMetadataExtractor.Extract(source, "content-placeholder");

        await Assert.That(meta.Imports.Count).IsEqualTo(1);
        await Assert.That(meta.Exports.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Golden coverage guard: every concrete <c>TsTopLevel</c> subtype
    /// shipped under <c>TypeScript/AST/</c> must be acknowledged by
    /// <see cref="FileMetadataExtractor.Extract"/> (either through a
    /// kind-specific case or one of the side-effect / non-exported
    /// fall-through cases). The default arm throws on anything else,
    /// so a freshly added subtype that no one wired in fails this
    /// test before it can silently miss the cache.
    /// <para>
    /// The placeholder table is hand-maintained on purpose. A new
    /// <c>TsTopLevel</c> subtype that doesn't appear in
    /// <see cref="PlaceholdersBySubtype"/> trips the
    /// <c>NotSupportedException</c> branch below — the contributor
    /// then has two choices: wire the subtype into the extractor's
    /// switch and add a placeholder here, or document why it cannot
    /// flow through the cache.
    /// </para>
    /// </summary>
    [Test]
    public async Task Extract_CoversEveryTsTopLevelSubtype()
    {
        var subtypes = typeof(TsTopLevel)
            .Assembly.GetTypes()
            .Where(t => t.IsSealed && !t.IsAbstract && typeof(TsTopLevel).IsAssignableFrom(t))
            .ToList();

        await Assert
            .That(subtypes.Count)
            .IsGreaterThan(0)
            .Because("Reflection failed to find any TsTopLevel subtype.");

        foreach (var subtype in subtypes)
        {
            if (!PlaceholdersBySubtype.TryGetValue(subtype, out var placeholder))
                throw new NotSupportedException(
                    $"Add a placeholder for TsTopLevel subtype {subtype.Name} to "
                        + "FileMetadataExtractorTests.PlaceholdersBySubtype, then "
                        + "ensure FileMetadataExtractor.Extract recognises it."
                );

            var source = new TsSourceFile("coverage.ts", [placeholder]);
            FileMetadataExtractor.Extract(source, "ignored");
        }
    }

    /// <summary>
    /// Minimal valid instances per TsTopLevel subtype. The contract
    /// the test enforces is: <c>FileMetadataExtractor.Extract</c>
    /// accepts every shape (either as an export or as a known
    /// no-op). Empty member lists / placeholder names suffice — the
    /// extractor never reads the bodies.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, TsTopLevel> PlaceholdersBySubtype =
        new Dictionary<Type, TsTopLevel>
        {
            [typeof(TsClass)] = new TsClass("CCls", null, []),
            [typeof(TsFunction)] = new TsFunction("fFn", [], new TsVoidType(), []),
            [typeof(TsEnum)] = new TsEnum("EEn", []),
            [typeof(TsConstObject)] = new TsConstObject("CoCo", []),
            [typeof(TsNamespaceDeclaration)] = new TsNamespaceDeclaration("NNs", []),
            [typeof(TsTypeAlias)] = new TsTypeAlias("TTa", new TsAnyType()),
            [typeof(TsInterface)] = new TsInterface("IIf", []),
            [typeof(TsImport)] = new TsImport(["X"], "#/x"),
            [typeof(TsReExport)] = new TsReExport(["X"], "#/x"),
            [typeof(TsModuleExport)] = new TsModuleExport("Mod", false),
            [typeof(TsExportImportAlias)] = new TsExportImportAlias("Alias", "Target"),
            [typeof(TsTopLevelStatement)] = new TsTopLevelStatement(new TsRawStatement(";")),
        };
}
