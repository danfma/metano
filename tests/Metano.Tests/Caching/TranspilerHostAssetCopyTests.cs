using Metano.Compiler;
using Metano.Compiler.Diagnostics;

namespace Metano.Tests.Caching;

/// <summary>
/// Pin the asset-copy contract that <see cref="TranspilerHost"/>
/// applies after <c>EmitFilesAsync</c>. Covers the sandbox checks
/// (rooted destinations, `..` escapes), missing-source handling,
/// and the happy-path copy with both mirrored and explicit
/// destinations.
/// </summary>
public class TranspilerHostAssetCopyTests
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
    public async Task CopyAssetsAsync_NoAssets_ReturnsEmpty()
    {
        var (projectDir, outputDir) = MakeWorkspace();
        var diagnostics = await TranspilerHost.CopyAssetsAsync([], outputDir, projectDir);
        await Assert.That(diagnostics.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CopyAssetsAsync_MirroredDestination_CopiesAlongsideSource()
    {
        var (projectDir, outputDir) = MakeWorkspace();
        var assetPath = Path.Combine(projectDir, "schema.sql");
        await File.WriteAllTextAsync(assetPath, "CREATE TABLE t (id INTEGER);");

        var diagnostics = await TranspilerHost.CopyAssetsAsync(
            [new AssetSpec(assetPath)],
            outputDir,
            projectDir
        );

        await Assert.That(diagnostics.Count).IsEqualTo(0);
        var landed = Path.Combine(outputDir, "schema.sql");
        await Assert.That(File.Exists(landed)).IsTrue();
        await Assert
            .That(await File.ReadAllTextAsync(landed))
            .IsEqualTo("CREATE TABLE t (id INTEGER);");
    }

    [Test]
    public async Task CopyAssetsAsync_ExplicitDestination_OverridesMirror()
    {
        var (projectDir, outputDir) = MakeWorkspace();
        var assetPath = Path.Combine(projectDir, "schema.sql");
        await File.WriteAllTextAsync(assetPath, "CREATE TABLE t (id INTEGER);");

        var diagnostics = await TranspilerHost.CopyAssetsAsync(
            [new AssetSpec(assetPath, RelativeDestination: "provider/schema.sql")],
            outputDir,
            projectDir
        );

        await Assert.That(diagnostics.Count).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(outputDir, "provider", "schema.sql"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outputDir, "schema.sql"))).IsFalse();
    }

    [Test]
    public async Task CopyAssetsAsync_MissingSource_ReportsErrorMs0025()
    {
        var (projectDir, outputDir) = MakeWorkspace();
        var diagnostics = await TranspilerHost.CopyAssetsAsync(
            [new AssetSpec(Path.Combine(projectDir, "missing.sql"))],
            outputDir,
            projectDir
        );

        await Assert.That(diagnostics.Count).IsEqualTo(1);
        await Assert.That(diagnostics[0].Code).IsEqualTo(DiagnosticCodes.AssetCopyFailure);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task CopyAssetsAsync_ParentEscape_ReportsError_DoesNotWrite()
    {
        var (projectDir, outputDir) = MakeWorkspace();
        var assetPath = Path.Combine(projectDir, "schema.sql");
        await File.WriteAllTextAsync(assetPath, "x");

        var diagnostics = await TranspilerHost.CopyAssetsAsync(
            [new AssetSpec(assetPath, RelativeDestination: "../escape.sql")],
            outputDir,
            projectDir
        );

        await Assert.That(diagnostics.Count).IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert
            .That(File.Exists(Path.Combine(Path.GetDirectoryName(outputDir)!, "escape.sql")))
            .IsFalse();
    }

    [Test]
    public async Task CopyAssetsAsync_RootedDestination_ReportsError()
    {
        var (projectDir, outputDir) = MakeWorkspace();
        var assetPath = Path.Combine(projectDir, "schema.sql");
        await File.WriteAllTextAsync(assetPath, "x");

        var diagnostics = await TranspilerHost.CopyAssetsAsync(
            [new AssetSpec(assetPath, RelativeDestination: "/etc/escape.sql")],
            outputDir,
            projectDir
        );

        await Assert.That(diagnostics.Count).IsEqualTo(1);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
    }

    [Test]
    public async Task CopyAssetsAsync_MultipleAssets_CollectsAllDiagnostics()
    {
        var (projectDir, outputDir) = MakeWorkspace();
        var goodAsset = Path.Combine(projectDir, "good.sql");
        await File.WriteAllTextAsync(goodAsset, "y");

        var diagnostics = await TranspilerHost.CopyAssetsAsync(
            [
                new AssetSpec(goodAsset),
                new AssetSpec(Path.Combine(projectDir, "missing.sql")),
                new AssetSpec(goodAsset, RelativeDestination: "../escape.sql"),
            ],
            outputDir,
            projectDir
        );

        await Assert.That(diagnostics.Count).IsEqualTo(2);
        await Assert
            .That(diagnostics.All(d => d.Severity == MetanoDiagnosticSeverity.Error))
            .IsTrue();
        await Assert.That(File.Exists(Path.Combine(outputDir, "good.sql"))).IsTrue();
    }

    private (string ProjectDir, string OutputDir) MakeWorkspace()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "metano-asset-test-" + Guid.NewGuid().ToString("N")
        );
        var projectDir = Path.Combine(root, "project");
        var outputDir = Path.Combine(root, "output");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(outputDir);
        _tempDirs.Add(root);
        return (projectDir, outputDir);
    }
}
