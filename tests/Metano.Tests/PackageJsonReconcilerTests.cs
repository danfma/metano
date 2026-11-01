using Metano.Compiler;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Tests;

/// <summary>
/// #334: the package.json stage over a refused <see cref="PackageRootResolution"/>
/// writes nothing. It reports the refusal when the run has files to expose and is
/// skipped otherwise, like any absent package.json.
/// </summary>
public class PackageJsonReconcilerTests
{
    private const string UnrelatedPackageJson = """{ "name": "unrelated" }""";

    private readonly string _root = Directory.CreateTempSubdirectory("metano-reconciler-").FullName;

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task RefusedWithFilesToExpose_ReportsTheRefusalAndWritesNothing()
    {
        var unrelatedPackageJson = WriteUnrelatedPackageJson();
        var resolution = ResolveOutputAsProjectDirectory();

        var reconciliation = Reconcile(resolution, [new TsSourceFile("index.ts", [], "")]);

        await Assert.That(reconciliation.WasWritten).IsFalse();
        await Assert.That(reconciliation.HasErrors).IsTrue();
        await Assert.That(reconciliation.Diagnostics).IsEquivalentTo([resolution.Refusal!]);
        await Assert.That(File.ReadAllText(unrelatedPackageJson)).IsEqualTo(UnrelatedPackageJson);
        await Assert.That(File.Exists(Path.Combine(OutputDir, "package.json"))).IsFalse();
    }

    [Test]
    public async Task RefusedWithoutFilesToExpose_IsSkipped()
    {
        var resolution = ResolveOutputAsProjectDirectory();

        var reconciliation = Reconcile(resolution, []);

        await Assert.That(reconciliation).IsSameReferenceAs(PackageJsonReconciliation.Skipped);
        await Assert.That(reconciliation.HasErrors).IsFalse();
    }

    [Test]
    public async Task DeleteStamp_RemovesTheMSBuildStamp()
    {
        var stampPath = Path.Combine(OutputDir, ".metano-stamp");

        Directory.CreateDirectory(OutputDir);
        File.WriteAllText(stampPath, "");

        TranspilerHost.DeleteStamp(OutputDir);

        await Assert.That(File.Exists(stampPath)).IsFalse();
    }

    private string OutputDir => Path.Combine(_root, "app");

    /// <summary>The output is the project directory, with a package.json only above it.</summary>
    private PackageRootResolution ResolveOutputAsProjectDirectory() =>
        PackageRootResolver.Resolve(OutputDir, Path.Combine(OutputDir, "App.csproj"));

    private string WriteUnrelatedPackageJson()
    {
        var path = Path.Combine(_root, "package.json");

        File.WriteAllText(path, UnrelatedPackageJson);

        return path;
    }

    private PackageJsonReconciliation Reconcile(
        PackageRootResolution resolution,
        IReadOnlyList<TsSourceFile> sourceFiles
    ) =>
        PackageJsonReconciler.Reconcile(
            PackageManifestInputs.None with
            {
                SourceFiles = sourceFiles,
            },
            resolution,
            OutputDir,
            dist: "./dist",
            srcRoot: null,
            importAlias: null
        );
}
