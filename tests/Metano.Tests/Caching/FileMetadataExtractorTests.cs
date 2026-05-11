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

        var meta = FileMetadataExtractor.Extract(source);

        await Assert.That(meta.Path).IsEqualTo("foo/bar.ts");
        await Assert.That(meta.Imports.Count).IsEqualTo(1);
        await Assert.That(meta.Imports[0].From).IsEqualTo("#/shared-kernel/user-id");
        await Assert.That(meta.Imports[0].Names[0]).IsEqualTo("UserId");
        await Assert.That(meta.Exports.Count).IsEqualTo(2);
        await Assert.That(meta.Exports.Any(e => e.Name == "Bar" && !e.TypeOnly)).IsTrue();
        await Assert.That(meta.Exports.Any(e => e.Name == "BarOptions" && e.TypeOnly)).IsTrue();
    }

    [Test]
    public async Task Stub_Roundtrip_HasMatchingImportsAndExportMarkers()
    {
        var meta = new CachedFileMetadata(
            "foo/bar.ts",
            [new CachedImport(["UserId"], "#/shared-kernel/user-id")],
            [
                new CachedExport("Bar", TypeOnly: false),
                new CachedExport("BarOptions", TypeOnly: true),
            ]
        );

        var stub = FileMetadataExtractor.BuildStub(meta);

        await Assert.That(stub.FileName).IsEqualTo("foo/bar.ts");
        await Assert.That(stub.Statements.OfType<TsImport>().Count()).IsEqualTo(1);
        // Value export → TsConstObject with matching name.
        await Assert
            .That(stub.Statements.OfType<TsConstObject>().Any(c => c.Name == "Bar"))
            .IsTrue();
        // Type-only export → TsInterface.
        await Assert
            .That(stub.Statements.OfType<TsInterface>().Any(i => i.Name == "BarOptions"))
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

        var meta = FileMetadataExtractor.Extract(source);

        await Assert.That(meta.Imports.Count).IsEqualTo(1);
        await Assert.That(meta.Exports.Count).IsEqualTo(0);
    }
}
