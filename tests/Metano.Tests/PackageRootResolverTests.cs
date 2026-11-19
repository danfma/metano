using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// #334: without <c>--package-root</c>, the package root search walks up from the
/// output directory but never above the topmost directory of its workspace (a
/// <c>.git</c> repository root, a solution directory, the C# project directory, or the
/// child of the directory shared with the project, or the output directory itself when
/// it is unrelated to the project), so it cannot pick and rewrite an unrelated
/// <c>package.json</c> further up the filesystem.
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

        await AssertResolved(resolution, "repo/web", "repo/web/src/generated", "src/generated");
    }

    [Test]
    public async Task PackageJsonAtTheBoundary_IsTheRoot()
    {
        MarkRepository("repo");
        WritePackageJson("repo");

        var resolution = Resolve(output: "repo/web/src", project: "repo/api/Api.csproj");

        await AssertResolved(resolution, "repo", "repo/web/src", "web/src");
    }

    [Test]
    public async Task RepositoryRoot_TakesPrecedenceOverTheProjectDirectory()
    {
        MarkRepository("repo");
        WritePackageJson("repo");

        var resolution = Resolve(output: "repo/app/ts/src", project: "repo/app/App.csproj");

        await AssertResolved(resolution, "repo", "repo/app/ts/src", "app/ts/src");
    }

    [Test]
    public async Task RepositoryRoot_TakesPrecedenceOverANestedSolution()
    {
        MarkRepository("repo");
        WritePackageJson("repo");
        MarkSolution("repo/src", "App.sln");

        var resolution = Resolve(output: "repo/src/Web/gen", project: "repo/src/Web/Web.csproj");

        await AssertResolved(resolution, "repo", "repo/src/Web/gen", "src/Web/gen");
    }

    [Test]
    public async Task SolutionFilter_IsNotASolution()
    {
        // A real solution in `app/web` would stop the search below `app/package.json`.
        WritePackageJson("app");
        MarkSolution("app/web", "App.slnf");

        var resolution = Resolve(output: "app/web/src", project: "app/App.csproj");

        await AssertResolved(resolution, "app", "app/web/src", "web/src");
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

        await AssertResolved(
            resolution,
            "workspace/frontend",
            "workspace/frontend/src/abc/contracts",
            "src/abc/contracts"
        );
    }

    // ── Package roots above the workspace are ignored ──────────────────────

    [Test]
    public async Task PackageJsonAboveTheProjectDirectory_IsIgnored()
    {
        // The #334 repro: no marker, output inside the project, a stray package.json above.
        WritePackageJson(AboveWorkspace);

        var resolution = Resolve(output: "app/ts/src", project: "app/App.csproj");

        await AssertResolved(resolution, "app/ts", "app/ts/src", "src");
    }

    [Test]
    public async Task PackageJsonAboveTheRepositoryRoot_IsIgnored()
    {
        WritePackageJson(AboveWorkspace);
        MarkRepository("repo");

        var resolution = Resolve(output: "repo/web/src", project: "repo/api/Api.csproj");

        await AssertResolved(resolution, "repo/web", "repo/web/src", "src");
    }

    [Test]
    public async Task PackageJsonAboveAWorktreeRoot_IsIgnored()
    {
        WritePackageJson(AboveWorkspace);
        MarkWorktree("worktree");

        var resolution = Resolve(output: "worktree/web/src", project: "worktree/api/Api.csproj");

        await AssertResolved(resolution, "worktree/web", "worktree/web/src", "src");
    }

    [Test]
    [Arguments("App.sln")]
    [Arguments("App.slnx")]
    public async Task PackageJsonAboveTheSolutionDirectory_IsIgnored(string solutionFile)
    {
        WritePackageJson(AboveWorkspace);
        MarkSolution("solution", solutionFile);

        var resolution = Resolve(output: "solution/web/src", project: "solution/api/Api.csproj");

        await AssertResolved(resolution, "solution/web", "solution/web/src", "src");
    }

    [Test]
    public async Task WithoutMarkers_PackageJsonInTheDirectorySharedWithTheProject_IsIgnored()
    {
        // `~/code/Api` + `~/web/src/gen`: the shared `~` is a generic folder, never a root.
        WritePackageJson("home");

        var resolution = Resolve(output: "home/web/src/gen", project: "home/code/Api/Api.csproj");

        await AssertResolved(resolution, "home/web/src", "home/web/src/gen", "gen");
    }

    [Test]
    public async Task OutputUnrelatedToTheProject_PackageJsonInItsParent_IsIgnored()
    {
        // `-p ~/proj/P.csproj -o /tmp/out`: the trees share only the filesystem root.
        WritePackageJson("tmp");

        var resolution = Resolve(output: "tmp/out", project: ProjectOutsideTempRoot());

        await AssertRefused(resolution, outputDir: "tmp/out");
    }

    [Test]
    public async Task OutputUnrelatedToTheProject_PackageJsonInTheOutput_IsTheRoot()
    {
        WritePackageJson("tmp/out");

        var resolution = Resolve(output: "tmp/out", project: ProjectOutsideTempRoot());

        await AssertResolved(resolution, "tmp/out", "tmp/out", ".");
    }

    [Test]
    public async Task SolutionWithoutRepository_PackageJsonBelowIt_IsTheRoot()
    {
        WritePackageJson(AboveWorkspace);
        MarkSolution("solution", "App.slnx");
        WritePackageJson("solution/web");

        var resolution = Resolve(output: "solution/web/src", project: "elsewhere/Api.csproj");

        await AssertResolved(resolution, "solution/web", "solution/web/src", "src");
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

        await AssertResolved(resolution, "repo/web", "repo/web/src", "src");
    }

    [Test]
    public async Task OutputIsTheProjectDirectory_WithoutPackageJson_IsRefused()
    {
        WritePackageJson(AboveWorkspace);

        var resolution = Resolve(output: "app", project: "app/App.csproj");

        await AssertRefused(resolution, outputDir: "app");
    }

    [Test]
    public async Task OutputDirectlyInTheDirectorySharedWithTheProject_IsRefused()
    {
        WritePackageJson("tmp");

        var resolution = Resolve(output: "tmp/out", project: "tmp/app/App.csproj");

        await AssertRefused(resolution, outputDir: "tmp/out");
    }

    [Test]
    public async Task OutputHoldsTheProject_WithoutPackageJson_IsRefused()
    {
        WritePackageJson(AboveWorkspace);

        var resolution = Resolve(output: "out", project: "out/app/App.csproj");

        await AssertRefused(resolution, outputDir: "out");
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

        await AssertResolved(resolution, "app", "app", ".");
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

        await AssertResolved(resolution, AboveWorkspace, "app", "app");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SymlinkedWorkspace_UsesTheSameBoundaryForEitherPathSpelling(
        bool outputViaLink,
        bool existingPackage
    )
    {
        Directory.CreateDirectory(PathOf("workspace/app"));
        CreateDirectoryLink(PathOf("alias"), PathOf("workspace"));
        WritePackageJson(AboveWorkspace);

        if (existingPackage)
            WritePackageJson("workspace/app/generated");

        var resolution = Resolve(
            output: outputViaLink ? "alias/app/generated/src" : "workspace/app/generated/src",
            project: outputViaLink ? "workspace/app/App.csproj" : "alias/app/App.csproj"
        );

        await AssertResolved(
            resolution,
            outputViaLink ? "alias/app/generated" : "workspace/app/generated",
            outputViaLink ? "alias/app/generated/src" : "workspace/app/generated/src",
            "src"
        );
    }

    [Test]
    public async Task OutputLinkedToProjectAncestor_KeepsTheWrittenFallbackParent()
    {
        Directory.CreateDirectory(PathOf("proj/src/App"));
        CreateDirectoryLink(PathOf("proj/linksrc"), PathOf("proj/src"));

        var resolution = Resolve(output: "proj/linksrc/gen", project: "proj/src/App/App.csproj");

        await AssertResolved(resolution, "proj/linksrc", "proj/linksrc/gen", "gen");
    }

    [Test]
    public async Task SymlinkToUnrelatedOutput_KeepsTheWrittenFallbackParent()
    {
        Directory.CreateDirectory(PathOf("app"));
        Directory.CreateDirectory(PathOf("outside"));
        CreateDirectoryLink(PathOf("app/output-link"), PathOf("outside"));
        WritePackageJson(AboveWorkspace);

        var resolution = Resolve(output: "app/output-link", project: "app/App.csproj");

        await AssertResolved(resolution, "app", "app/output-link", "output-link");
    }

    [Test]
    public async Task OutputLinkedToSiblingTree_KeepsRepositoryPackageAndRelativePath()
    {
        MarkRepository("repo");
        WritePackageJson("repo");
        WritePackageJson("ext");
        Directory.CreateDirectory(PathOf("ext/gen"));
        CreateDirectoryLink(PathOf("repo/generated"), PathOf("ext/gen"));

        var resolution = Resolve(output: "repo/generated", project: "repo/App.csproj");

        await AssertResolved(resolution, "repo", "repo/generated", "generated");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task OutputLinkedToFarTree_KeepsWrittenPackageAndRelativePath(bool ownPackage)
    {
        // Keep the repo on the checkout's filesystem and the target under /var/tmp.
        // On the macOS review checkout these trees share only '/'. Markers must still
        // be searched on the written path, not on the far target's ancestors.
        var repo = Path.Combine(
            Directory.GetCurrentDirectory(),
            "obj",
            "metano-package-repo-" + Guid.NewGuid().ToString("N")
        );

        var farParent = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/var/tmp";

        var far = Directory.CreateDirectory(
            Path.Combine(farParent, "metano-package-far-" + Guid.NewGuid().ToString("N"))
        );

        try
        {
            MarkRepository(repo);
            WritePackageJson(repo);
            Directory.CreateDirectory(Path.Combine(repo, "packages"));
            var linkedOutput = Path.Combine(repo, ownPackage ? "packages/web" : "generated");
            CreateDirectoryLink(linkedOutput, far.FullName);

            if (ownPackage)
                WritePackageJson(linkedOutput);

            var output = ownPackage ? Path.Combine(linkedOutput, "src") : linkedOutput;
            var resolution = Resolve(output, Path.Combine(repo, "App.csproj"));

            await AssertResolved(
                resolution,
                ownPackage ? linkedOutput : repo,
                output,
                ownPackage ? "src" : "generated"
            );
        }
        finally
        {
            if (Directory.Exists(repo))
                Directory.Delete(repo, recursive: true);

            far.Delete(recursive: true);
        }
    }

    [Test]
    public async Task MacOsVarAlias_UsesWrittenRootAndWriterRelativePath()
    {
        if (!OperatingSystem.IsMacOS())
            Skip.Test("The /var -> /private/var alias is macOS-specific.");

        var written = PathOf("app/generated/src");

        var physical = written.StartsWith("/private/var/", StringComparison.Ordinal)
            ? written
            : "/private" + written;

        var project = written.StartsWith("/private/var/", StringComparison.Ordinal)
            ? PathOf("app/App.csproj")["/private".Length..]
            : PathOf("app/App.csproj");

        Directory.CreateDirectory(PathOf("app"));

        var resolution = PackageRootResolver.Resolve(physical, project);

        await Assert.That(resolution.IsRefused).IsFalse();
        await Assert.That(resolution.Root).IsEqualTo(Path.GetDirectoryName(physical));
        await Assert.That(Path.GetRelativePath(resolution.Root!, physical)).IsEqualTo("src");
    }

    [Test]
    public async Task ExplicitSymlinkedPackageRoot_IsStillUsedAsGiven()
    {
        Directory.CreateDirectory(PathOf("workspace"));
        CreateDirectoryLink(PathOf("alias"), PathOf("workspace"));

        var resolution = PackageRootResolver.Resolve(
            PathOf("workspace/generated/src"),
            PathOf("workspace/App.csproj"),
            explicitPackageRoot: PathOf("alias")
        );

        await AssertResolved(
            resolution,
            "alias",
            "workspace/generated/src",
            "../workspace/generated/src"
        );
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private PackageRootResolution Resolve(string output, string project) =>
        PackageRootResolver.Resolve(PathOf(output), PathOf(project));

    private string PathOf(string relative) =>
        Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// A project path whose only common ancestor with the temp root is the filesystem
    /// root: the output is unrelated to the project.
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

    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception)
            when (exception
                    is PlatformNotSupportedException
                        or UnauthorizedAccessException
                        or IOException
            )
        {
            Skip.Test($"Directory symlinks unavailable: {exception.Message}");
        }
    }

    private async Task AssertResolved(
        PackageRootResolution resolution,
        string expectedRoot,
        string output,
        string expectedRelative
    )
    {
        await Assert.That(resolution.IsRefused).IsFalse();
        await Assert.That(resolution.Root).IsEqualTo(PathOf(expectedRoot));

        await Assert
            .That(Path.GetRelativePath(resolution.Root!, PathOf(output)))
            .IsEqualTo(expectedRelative.Replace('/', Path.DirectorySeparatorChar));
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
