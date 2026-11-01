using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #334: without <c>--package-root</c>, the package root search walks up from the
/// output directory but never past the workspace boundary (a <c>.git</c> repository
/// root, a solution directory, or the directory that holds both the output and the C#
/// project), so it cannot pick and rewrite an unrelated <c>package.json</c> further up
/// the filesystem.
/// <para>
/// Each test builds its workspace inside a fresh temp directory and assumes that no
/// ancestor of the system temp directory is a repository root or holds a solution
/// file, as on developer machines and CI runners.
/// </para>
/// </summary>
public class PackageRootResolverTests
{
    /// <summary>The temp directory itself: above every workspace a test builds in it.</summary>
    private const string AboveWorkspace = "";

    private readonly string _root = Directory
        .CreateTempSubdirectory("metano-package-root-")
        .FullName;

    [After(Test)]
    public void DeleteTempDir()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── Search inside the workspace ────────────────────────────────────────

    [Test]
    public async Task PackageJsonBetweenOutputAndBoundary_IsTheRoot()
    {
        MarkRepository("repo");
        WritePackageJson("repo/web");

        var resolution = Resolve(output: "repo/web/src/generated", project: "repo/api/Api.csproj");

        await AssertResolved(resolution, "repo/web");
    }

    [Test]
    public async Task PackageJsonAtTheBoundary_IsTheRoot()
    {
        MarkRepository("repo");
        WritePackageJson("repo");

        var resolution = Resolve(output: "repo/web/src", project: "repo/api/Api.csproj");

        await AssertResolved(resolution, "repo");
    }

    [Test]
    public async Task RepositoryRoot_TakesPrecedenceOverTheProjectDirectory()
    {
        MarkRepository("repo");
        WritePackageJson("repo");

        var resolution = Resolve(output: "repo/app/ts/src", project: "repo/app/App.csproj");

        await AssertResolved(resolution, "repo");
    }

    [Test]
    public async Task RepositoryRoot_TakesPrecedenceOverANestedSolution()
    {
        MarkRepository("repo");
        WritePackageJson("repo");
        MarkSolution("repo/src", "App.sln");

        var resolution = Resolve(output: "repo/src/Web/gen", project: "repo/src/Web/Web.csproj");

        await AssertResolved(resolution, "repo");
    }

    [Test]
    public async Task SolutionFilter_IsNotASolution()
    {
        WritePackageJson("workspace");
        MarkSolution("workspace/filtered", "App.slnf");

        var resolution = Resolve(
            output: "workspace/filtered/web/src",
            project: "workspace/api/Api.csproj"
        );

        await AssertResolved(resolution, "workspace");
    }

    [Test]
    public async Task WithoutMarkers_PackageJsonOfASiblingFrontend_IsTheRoot()
    {
        // The spec 004 layout: a contracts project next to the frontend that hosts its output.
        WritePackageJson("workspace/frontend");

        var resolution = Resolve(
            output: "workspace/frontend/src/abc/contracts",
            project: "workspace/Abc.Contracts/Abc.Contracts.csproj"
        );

        await AssertResolved(resolution, "workspace/frontend");
    }

    // ── Package roots above the workspace are ignored ──────────────────────

    [Test]
    public async Task PackageJsonAboveTheProjectDirectory_IsIgnored()
    {
        // The #334 repro: no marker, output inside the project, a stray package.json above.
        WritePackageJson(AboveWorkspace);

        var resolution = Resolve(output: "app/ts/src", project: "app/App.csproj");

        await AssertResolved(resolution, "app/ts");
    }

    [Test]
    public async Task PackageJsonAboveTheRepositoryRoot_IsIgnored()
    {
        WritePackageJson(AboveWorkspace);
        MarkRepository("repo");

        var resolution = Resolve(output: "repo/web/src", project: "repo/api/Api.csproj");

        await AssertResolved(resolution, "repo/web");
    }

    [Test]
    public async Task PackageJsonAboveAWorktreeRoot_IsIgnored()
    {
        WritePackageJson(AboveWorkspace);
        MarkWorktree("worktree");

        var resolution = Resolve(output: "worktree/web/src", project: "worktree/api/Api.csproj");

        await AssertResolved(resolution, "worktree/web");
    }

    [Test]
    [Arguments("App.sln")]
    [Arguments("App.slnx")]
    public async Task PackageJsonAboveTheSolutionDirectory_IsIgnored(string solutionFile)
    {
        WritePackageJson(AboveWorkspace);
        MarkSolution("solution", solutionFile);

        var resolution = Resolve(output: "solution/web/src", project: "solution/api/Api.csproj");

        await AssertResolved(resolution, "solution/web");
    }

    [Test]
    public async Task WithoutAnyBoundary_PackageJsonAboveTheParent_IsIgnored()
    {
        WritePackageJson(AboveWorkspace);

        var resolution = Resolve(output: "a/b/c", project: ProjectOutsideTempRoot());

        await AssertResolved(resolution, "a/b");
    }

    [Test]
    public async Task WithoutAnyBoundary_PackageJsonInTheParent_IsTheRoot()
    {
        WritePackageJson("a/b");

        var resolution = Resolve(output: "a/b/c", project: ProjectOutsideTempRoot());

        await AssertResolved(resolution, "a/b");
    }

    // ── Fallback and refusal ───────────────────────────────────────────────

    [Test]
    public async Task TrailingSeparatorOnOutput_FallsBackToItsParent()
    {
        MarkRepository("repo");

        var resolution = PackageRootResolver.Resolve(
            PathOf("repo/web/src") + Path.DirectorySeparatorChar,
            PathOf("repo/api/Api.csproj")
        );

        await AssertResolved(resolution, "repo/web");
    }

    [Test]
    public async Task OutputIsTheProjectDirectory_WithoutPackageJson_IsRefused()
    {
        WritePackageJson(AboveWorkspace);

        var resolution = Resolve(output: "app", project: "app/App.csproj");

        await AssertRefused(resolution, outputDir: "app");
    }

    [Test]
    public async Task OutputIsTheRepositoryRoot_WithoutPackageJson_IsRefused()
    {
        WritePackageJson(AboveWorkspace);
        MarkRepository("repo");

        var resolution = Resolve(output: "repo", project: "repo/api/Api.csproj");

        await AssertRefused(resolution, outputDir: "repo");
    }

    [Test]
    public async Task OutputIsTheProjectDirectory_WithPackageJson_IsTheRoot()
    {
        WritePackageJson("app");

        var resolution = Resolve(output: "app", project: "app/App.csproj");

        await AssertResolved(resolution, "app");
    }

    [Test]
    public async Task ExplicitPackageRoot_IsUsedAsGiven()
    {
        WritePackageJson(AboveWorkspace);

        var resolution = PackageRootResolver.Resolve(
            PathOf("app"),
            PathOf("app/App.csproj"),
            explicitPackageRoot: PathOf(AboveWorkspace)
        );

        await AssertResolved(resolution, AboveWorkspace);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private PackageRootResolution Resolve(string output, string project) =>
        PackageRootResolver.Resolve(PathOf(output), PathOf(project));

    private string PathOf(string relative) =>
        Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// A project path whose only common ancestor with the temp root is the filesystem
    /// root, so no project-based boundary exists.
    /// </summary>
    private string ProjectOutsideTempRoot() =>
        Path.Combine(Path.GetPathRoot(_root)!, "metano-334-elsewhere", "App.csproj");

    private void WritePackageJson(string relativeDir)
    {
        Directory.CreateDirectory(PathOf(relativeDir));
        File.WriteAllText(Path.Combine(PathOf(relativeDir), "package.json"), "{}");
    }

    private void MarkRepository(string relativeDir) =>
        Directory.CreateDirectory(Path.Combine(PathOf(relativeDir), ".git"));

    private void MarkWorktree(string relativeDir)
    {
        Directory.CreateDirectory(PathOf(relativeDir));

        File.WriteAllText(
            Path.Combine(PathOf(relativeDir), ".git"),
            "gitdir: /elsewhere/.git/worktrees/x\n"
        );
    }

    private void MarkSolution(string relativeDir, string solutionFile)
    {
        Directory.CreateDirectory(PathOf(relativeDir));
        File.WriteAllText(Path.Combine(PathOf(relativeDir), solutionFile), "");
    }

    private async Task AssertResolved(PackageRootResolution resolution, string expectedRoot)
    {
        await Assert.That(resolution.IsRefused).IsFalse();
        await Assert.That(resolution.Root).IsEqualTo(PathOf(expectedRoot));
    }

    private async Task AssertRefused(PackageRootResolution resolution, string outputDir)
    {
        await Assert.That(resolution.IsRefused).IsTrue();
        await Assert.That(resolution.Root).IsNull();

        var refusal = resolution.Refusal!;

        await Assert.That(refusal.Code).IsEqualTo(DiagnosticCodes.PackageRootOutsideWorkspace);
        await Assert.That(refusal.Severity).IsEqualTo(MetanoDiagnosticSeverity.Error);
        await Assert.That(refusal.Message).Contains($"'{PathOf(outputDir)}'");
    }
}
