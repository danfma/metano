using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #334: without <c>--package-root</c>, the package root search walks up from the
/// output directory but never past the workspace boundary (the C# project directory, a
/// <c>.git</c> repository root or a solution directory), so it cannot pick and rewrite
/// an unrelated <c>package.json</c> further up the filesystem.
/// </summary>
public class PackageRootResolverTests
{
    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-package-root-")
        .FullName;

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task PackageJsonBetweenOutputAndBoundary_IsTheRoot()
    {
        MarkRepository("repo");
        WritePackageJson("repo/web");

        var resolution = Resolve(output: "repo/web/src/generated", project: "repo/api");

        await AssertResolved(resolution, "repo/web");
    }

    [Test]
    public async Task PackageJsonAtTheBoundary_IsTheRoot()
    {
        MarkRepository("repo");
        WritePackageJson("repo");

        var resolution = Resolve(output: "repo/web/src", project: "repo/api");

        await AssertResolved(resolution, "repo");
    }

    [Test]
    public async Task PackageJsonAboveProjectDirectory_IsIgnored()
    {
        WritePackageJson("");

        var resolution = Resolve(output: "app/ts/src", project: "app");

        await AssertResolved(resolution, "app/ts");
    }

    [Test]
    public async Task PackageJsonAboveRepositoryRoot_IsIgnored()
    {
        WritePackageJson("");
        MarkRepository("repo");

        var resolution = Resolve(output: "repo/web/src", project: "repo/api");

        await AssertResolved(resolution, "repo/web");
    }

    [Test]
    public async Task GitFile_MarksAWorktreeRoot()
    {
        WritePackageJson("");
        Directory.CreateDirectory(PathOf("worktree"));
        File.WriteAllText(PathOf("worktree/.git"), "gitdir: /elsewhere/.git/worktrees/x\n");

        var resolution = Resolve(output: "worktree/web/src", project: "worktree/api");

        await AssertResolved(resolution, "worktree/web");
    }

    [Test]
    [Arguments("App.sln")]
    [Arguments("App.slnx")]
    public async Task PackageJsonAboveSolutionDirectory_IsIgnored(string solutionFile)
    {
        WritePackageJson("");
        Directory.CreateDirectory(PathOf("solution"));
        File.WriteAllText(PathOf($"solution/{solutionFile}"), "");

        var resolution = Resolve(output: "solution/web/src", project: "solution/api");

        await AssertResolved(resolution, "solution/web");
    }

    [Test]
    public async Task TrailingSeparatorOnOutput_FallsBackToItsParent()
    {
        MarkRepository("repo");

        var resolution = PackageRootResolver.Resolve(
            PathOf("repo/web/src") + Path.DirectorySeparatorChar,
            PathOf("repo/api")
        );

        await AssertResolved(resolution, "repo/web");
    }

    [Test]
    public async Task OutputIsTheProjectDirectory_WithoutPackageJson_IsRefused()
    {
        WritePackageJson("");

        var resolution = Resolve(output: "app", project: "app");

        await AssertRefused(resolution, outputDir: "app");
    }

    [Test]
    public async Task OutputIsTheRepositoryRoot_WithoutPackageJson_IsRefused()
    {
        WritePackageJson("");
        MarkRepository("repo");

        var resolution = Resolve(output: "repo", project: "repo/api");

        await AssertRefused(resolution, outputDir: "repo");
    }

    [Test]
    public async Task OutputIsTheProjectDirectory_WithPackageJson_IsTheRoot()
    {
        WritePackageJson("app");

        var resolution = Resolve(output: "app", project: "app");

        await AssertResolved(resolution, "app");
    }

    private PackageRootResolution Resolve(string output, string project) =>
        PackageRootResolver.Resolve(PathOf(output), PathOf(project));

    private string PathOf(string relative) =>
        Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private void WritePackageJson(string relativeDir)
    {
        Directory.CreateDirectory(PathOf(relativeDir));
        File.WriteAllText(Path.Combine(PathOf(relativeDir), "package.json"), "{}");
    }

    private void MarkRepository(string relativeDir) =>
        Directory.CreateDirectory(Path.Combine(PathOf(relativeDir), ".git"));

    private async Task AssertResolved(PackageRootResolution resolution, string expectedRoot)
    {
        await Assert.That(resolution.IsRefused).IsFalse();
        await Assert.That(resolution.Root).IsEqualTo(PathOf(expectedRoot));
    }

    private async Task AssertRefused(PackageRootResolution resolution, string outputDir)
    {
        await Assert.That(resolution.IsRefused).IsTrue();
        await Assert.That(resolution.Root).IsNull();
        await Assert
            .That(resolution.Refusal!.Code)
            .IsEqualTo(DiagnosticCodes.PackageRootOutsideWorkspace);
        await Assert.That(resolution.Refusal.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(resolution.Refusal.Message).Contains($"'{PathOf(outputDir)}'");
    }
}
