using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metano.Compiler.Diagnostics;

namespace Metano.Packaging;

/// <summary>
/// Chooses the consumer package root: the directory whose <c>package.json</c> the
/// TypeScript run updates. An explicit <c>--package-root</c> is used as given.
/// <para>
/// Otherwise the search walks up from the output directory and takes the first
/// directory that holds a <c>package.json</c>, as npm and Bun do. Unlike their walk, it
/// never goes above the topmost directory of the workspace, so a <c>package.json</c>
/// that belongs to another project further up is not picked (#334). Starting at the
/// output directory itself, the topmost directory is:
/// <list type="number">
/// <item>the nearest repository root (it holds <c>.git</c>);</item>
/// <item>else the nearest solution directory (it holds a <c>.sln</c> or <c>.slnx</c>
/// file);</item>
/// <item>else the C# project directory, when it holds the output directory;</item>
/// <item>else, when the output and the project directory share a directory other than
/// the filesystem root, the directory one level below that shared directory on the
/// output's path, or the output directory itself when it is the shared directory. The
/// shared directory is often a generic folder such as a home or temp directory, so it is
/// never taken;</item>
/// <item>else (the output is unrelated to the project, which shares only the filesystem
/// root with it) the output directory itself.</item>
/// </list>
/// </para>
/// <para>
/// A <c>package.json</c> that declares <c>workspaces</c> is a workspace boundary, not a
/// package: the walk stops below it, and the fallback below applies (#764). A fallback
/// that would itself be such a workspace root is refused the same way as one outside the
/// workspace, so a workspace root's <c>package.json</c> is never written.
/// </para>
/// <para>
/// When the search finds nothing, the root falls back to the legacy default, the
/// parent of the output directory. When that parent lies above the topmost directory
/// (or the output directory is the filesystem root), the resolution is refused with
/// <see cref="DiagnosticCodes.PackageRootOutsideWorkspace"/>.
/// </para>
/// <para>
/// Known trade-offs: a repository root takes precedence, so a home directory kept
/// under git (dotfiles) widens the workspace to the home directory, and a monorepo's
/// root <c>package.json</c> wins when the generated package has none of its own. The search
/// follows the written output ancestors; only project-containment comparisons resolve
/// existing directory symbolic links, including ancestors of missing outputs, so macOS's
/// /var and /private/var aliases compare equal without moving package ownership or changing
/// writer-relative paths. An explicit root is used as given. Comparisons ignore case on
/// Windows and macOS.
/// </para>
/// </summary>
public static class PackageRootResolver
{
    private static readonly string[] SolutionFileExtensions = [".sln", ".slnx"];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Resolves the package root for the files generated into
    /// <paramref name="outputDir"/> from the C# project at <paramref name="projectPath"/>.
    /// A non-blank <paramref name="explicitPackageRoot"/> wins without any search; a blank one
    /// counts as unset (#353).
    /// </summary>
    public static PackageRootResolution Resolve(
        string outputDir,
        string projectPath,
        string? explicitPackageRoot = null
    )
    {
        if (!string.IsNullOrWhiteSpace(explicitPackageRoot))
            return PackageRootResolution.Resolved(Path.GetFullPath(explicitPackageRoot));

        var outputDirectory = NormalizeDirectory(outputDir);
        var projectDirectory = ProjectDirectoryOf(projectPath);
        var topmostDirectory = FindTopmostDirectory(outputDirectory, projectDirectory);
        var defaultPackageRoot = Path.GetDirectoryName(outputDirectory);

        var nearestPackageJsonDirectory = SelfAndAncestorsUpTo(outputDirectory, topmostDirectory)
            .TakeWhile(dir => !IsWorkspaceRoot(dir))
            .FirstOrDefault(HasPackageJson);

        if (nearestPackageJsonDirectory is not null)
            return PackageRootResolution.Resolved(nearestPackageJsonDirectory);

        if (
            FallbackLeavesWorkspace(outputDirectory, topmostDirectory, defaultPackageRoot)
            || IsWorkspaceRoot(defaultPackageRoot)
        )
            return PackageRootResolution.Refused(
                CreateOutsideWorkspaceDiagnostic(outputDirectory, defaultPackageRoot)
            );

        return PackageRootResolution.Resolved(defaultPackageRoot);
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string PhysicalDirectory(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var parent = Path.GetDirectoryName(fullPath);

        if (parent is null)
            return fullPath;

        var directory = new DirectoryInfo(
            Path.Combine(PhysicalDirectory(parent), Path.GetFileName(fullPath))
        );

        // Resolve each existing ancestor: the leaf need not exist, and resolving only the
        // leaf would miss /var -> /private/var and other links in the middle of the path.
        return directory.Exists && directory.LinkTarget is not null
            ? PhysicalDirectory(directory.ResolveLinkTarget(returnFinalTarget: true)!.FullName)
            : directory.FullName;
    }

    private static string ProjectDirectoryOf(string projectPath) =>
        NormalizeDirectory(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);

    /// <summary>
    /// The topmost directory the search may use as the package root; see the class
    /// summary for the ranking.
    /// </summary>
    private static string FindTopmostDirectory(string outputDirectory, string projectDirectory)
    {
        var candidates = SelfAndAncestors(outputDirectory).ToList();

        var markedRoot = candidates.FirstOrDefault(IsRepositoryRoot)
            ?? candidates.FirstOrDefault(IsSolutionDirectory);

        if (markedRoot is not null)
            return markedRoot;

        var sharedAncestor = candidates.FirstOrDefault(dir =>
            IsSharedWithProject(dir, projectDirectory)
        ) ?? candidates.FirstOrDefault(dir => IsPhysicallySharedWithProject(dir, projectDirectory));

        // Unrelated to the project: only a package.json in the output directory itself.
        if (sharedAncestor is null)
            return outputDirectory;

        if (IsSamePath(PhysicalDirectory(sharedAncestor), PhysicalDirectory(projectDirectory)))
            return sharedAncestor;

        // The shared directory is often generic (a home or temp folder): stop below it.
        return candidates.TakeWhile(dir => !IsSamePath(dir, sharedAncestor)).LastOrDefault()
            ?? outputDirectory;
    }

    /// <summary>
    /// The default package root, the parent of the output directory, leaves the workspace
    /// when there is no parent or when the output directory is already the topmost
    /// directory.
    /// </summary>
    private static bool FallbackLeavesWorkspace(
        string outputDirectory,
        string topmostDirectory,
        [NotNullWhen(false)] string? defaultPackageRoot
    ) => defaultPackageRoot is null || IsSamePath(outputDirectory, topmostDirectory);

    private static bool IsRepositoryRoot(string dir)
    {
        // A worktree or submodule stores `.git` as a file, a main checkout as a folder.
        var gitPath = Path.Combine(dir, ".git");

        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static bool IsSolutionDirectory(string dir)
    {
        try
        {
            // `*.sln` alone would also match `.slnf` filters on Windows; check the extension.
            return Directory.EnumerateFiles(dir, "*.sln*").Any(IsSolutionFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A missing (not yet created) or unreadable directory holds no solution.
            return false;
        }
    }

    private static bool IsSolutionFile(string file) =>
        SolutionFileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    private static bool IsSharedWithProject(string dir, string projectDirectory) =>
        !IsFilesystemRoot(dir) && IsSameOrUnder(projectDirectory, dir);

    private static bool IsPhysicallySharedWithProject(string dir, string projectDirectory)
    {
        // Only fall back to aliases when the written ancestor search finds no boundary.
        var physicalProjectDirectory = PhysicalDirectory(projectDirectory);
        var physicalAncestor = PhysicalDirectory(dir);

        return !IsFilesystemRoot(dir) && IsSameOrUnder(physicalProjectDirectory, physicalAncestor);
    }

    private static bool HasPackageJson(string dir) =>
        File.Exists(Path.Combine(dir, PackageJsonWriter.PackageJsonFileName));

    /// <summary>
    /// Whether <paramref name="dir"/> holds a <c>package.json</c> that declares
    /// <c>workspaces</c>: a workspace boundary that groups packages and is never a package
    /// itself (#764). An unreadable or malformed file declares nothing.
    /// </summary>
    private static bool IsWorkspaceRoot(string dir)
    {
        var path = Path.Combine(dir, PackageJsonWriter.PackageJsonFileName);

        if (!File.Exists(path))
            return false;

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) is JsonObject manifest
                && manifest.ContainsKey("workspaces");
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsFilesystemRoot(string dir) => Path.GetDirectoryName(dir) is null;

    private static bool IsSamePath(string path, string? other) =>
        string.Equals(path, other, PathComparison);

    private static bool IsSameOrUnder(string path, string ancestor)
    {
        var ancestorPrefix = Path.EndsInDirectorySeparator(ancestor)
            ? ancestor
            : ancestor + Path.DirectorySeparatorChar;

        return IsSamePath(path, ancestor) || path.StartsWith(ancestorPrefix, PathComparison);
    }

    private static IEnumerable<string> SelfAndAncestors(string dir)
    {
        for (var current = dir; current is not null; current = Path.GetDirectoryName(current))
            yield return current;
    }

    private static IEnumerable<string> SelfAndAncestorsUpTo(string dir, string topmost)
    {
        foreach (var current in SelfAndAncestors(dir))
        {
            yield return current;

            if (IsSamePath(current, topmost))
                yield break;
        }
    }

    private static MetanoDiagnostic CreateOutsideWorkspaceDiagnostic(
        string outputDirectory,
        string? defaultPackageRoot
    )
    {
        var cause = defaultPackageRoot is null
            ? "the output directory is the filesystem root, so there is no default package root"
            : $"the default package root (the parent of the output directory, '{defaultPackageRoot}') "
                + "cannot be used: it lies outside the workspace or is a workspace root "
                + "(a package.json declaring workspaces), which is never written (see the "
                + "package root rules in docs/cross-package.md)";

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.PackageRootOutsideWorkspace,
            $"No package.json was found for the output directory '{outputDirectory}' inside "
                + $"its workspace, and {cause}. package.json was not written. Pass "
                + "--package-root (MSBuild: MetanoPackageRoot) to choose the package root, or "
                + "--skip-package-json (MetanoSkipPackageJson) to skip the update."
        );
    }
}

/// <summary>
/// The outcome of <see cref="PackageRootResolver.Resolve"/>: the package root, or the
/// diagnostic that explains why no root inside the workspace could be chosen.
/// </summary>
public sealed class PackageRootResolution
{
    private PackageRootResolution(string? root, MetanoDiagnostic? refusal)
    {
        Root = root;
        Refusal = refusal;
    }

    /// <summary>Whether no package root was chosen; <see cref="Refusal"/> says why.</summary>
    [MemberNotNullWhen(true, nameof(Refusal))]
    [MemberNotNullWhen(false, nameof(Root))]
    public bool IsRefused => Refusal is not null;

    /// <summary>The package root directory; <c>null</c> when the resolution was refused.</summary>
    public string? Root { get; }

    /// <summary>Why no package root was chosen; <c>null</c> when <see cref="Root"/> is set.</summary>
    public MetanoDiagnostic? Refusal { get; }

    public static PackageRootResolution Resolved(string root) => new(root, null);

    public static PackageRootResolution Refused(MetanoDiagnostic refusal) => new(null, refusal);
}
