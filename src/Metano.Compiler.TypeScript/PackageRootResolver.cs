using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.Diagnostics;

namespace Metano;

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
/// <item>else, below the smallest directory that holds both the output and the project
/// directory (other than the filesystem root), the child of it on the output's path:
/// that shared parent is often a generic folder such as a home or temp directory, so it
/// is never taken (the output directory itself when it holds the project);</item>
/// <item>else (no boundary) the parent of the output directory.</item>
/// </list>
/// </para>
/// <para>
/// When the search finds nothing, the root falls back to the legacy default, the
/// parent of the output directory. When that parent lies above the topmost directory
/// (or the output directory is the filesystem root), the resolution is refused with
/// <see cref="DiagnosticCodes.PackageRootOutsideWorkspace"/>.
/// </para>
/// <para>
/// Known trade-offs: a repository root takes precedence, so a home directory kept
/// under git (dotfiles) widens the workspace to the home directory. Paths are compared
/// as given after <see cref="Path.GetFullPath(string)"/>: symbolic links are not
/// resolved, so the boundaries follow the paths as written, and the comparison ignores
/// case on Windows and macOS.
/// </para>
/// </summary>
public static class PackageRootResolver
{
    internal const string PackageJsonFileName = "package.json";

    private static readonly string[] SolutionFileExtensions = [".sln", ".slnx"];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Resolves the package root for the files generated into
    /// <paramref name="outputDir"/> from the C# project at <paramref name="projectPath"/>.
    /// A non-null <paramref name="explicitPackageRoot"/> wins without any search.
    /// </summary>
    public static PackageRootResolution Resolve(
        string outputDir,
        string projectPath,
        string? explicitPackageRoot = null
    )
    {
        if (explicitPackageRoot is not null)
            return PackageRootResolution.Resolved(Path.GetFullPath(explicitPackageRoot));

        var outputDirectory = NormalizeDirectory(outputDir);
        var projectDirectory = ProjectDirectoryOf(projectPath);
        var topmostDirectory = FindTopmostDirectory(outputDirectory, projectDirectory);
        var legacyRoot = Path.GetDirectoryName(outputDirectory);

        var nearestPackageJsonDirectory = SelfAndAncestorsUpTo(outputDirectory, topmostDirectory)
            .FirstOrDefault(HasPackageJson);

        if (nearestPackageJsonDirectory is not null)
            return PackageRootResolution.Resolved(nearestPackageJsonDirectory);

        if (FallbackLeavesWorkspace(outputDirectory, topmostDirectory, legacyRoot))
            return PackageRootResolution.Refused(
                CreateOutsideWorkspaceDiagnostic(outputDirectory, legacyRoot)
            );

        return PackageRootResolution.Resolved(legacyRoot);
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string ProjectDirectoryOf(string projectPath) =>
        NormalizeDirectory(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);

    /// <summary>
    /// The topmost directory the search may use as the package root; see the class
    /// summary for the ranking.
    /// </summary>
    private static string FindTopmostDirectory(string outputDirectory, string projectDirectory)
    {
        var candidates = SelfAndAncestors(outputDirectory).ToList();

        var markedRoot =
            candidates.FirstOrDefault(IsRepositoryRoot)
            ?? candidates.FirstOrDefault(IsSolutionDirectory);

        if (markedRoot is not null)
            return markedRoot;

        var sharedAncestorIndex = candidates.FindIndex(dir =>
            IsSharedAncestorBoundary(dir, projectDirectory)
        );

        if (sharedAncestorIndex < 0)
            return Path.GetDirectoryName(outputDirectory) ?? outputDirectory;

        var sharedAncestor = candidates[sharedAncestorIndex];

        if (IsSamePath(sharedAncestor, projectDirectory) || sharedAncestorIndex == 0)
            return sharedAncestor;

        return candidates[sharedAncestorIndex - 1];
    }

    /// <summary>
    /// The legacy fallback, the parent of the output directory, leaves the workspace
    /// when there is no parent or when the output directory is already the topmost
    /// directory.
    /// </summary>
    private static bool FallbackLeavesWorkspace(
        string outputDirectory,
        string topmostDirectory,
        [NotNullWhen(false)] string? legacyRoot
    ) => legacyRoot is null || IsSamePath(outputDirectory, topmostDirectory);

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

    private static bool IsSharedAncestorBoundary(string dir, string projectDirectory) =>
        !IsFilesystemRoot(dir) && IsSameOrUnder(projectDirectory, dir);

    private static bool HasPackageJson(string dir) =>
        File.Exists(Path.Combine(dir, PackageJsonFileName));

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
        string? legacyRoot
    )
    {
        var cause = legacyRoot is null
            ? "the output directory is the filesystem root, so there is no default package root"
            : $"the default package root (the parent of the output directory, '{legacyRoot}') "
                + "lies outside the workspace: the output directory is already the topmost "
                + "directory the search may use (a repository root, a solution directory, the "
                + "C# project directory, or the child of the directory shared with the project)";

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
