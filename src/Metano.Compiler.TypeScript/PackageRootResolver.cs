using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.Diagnostics;

namespace Metano;

/// <summary>
/// Chooses the consumer package root: the directory whose <c>package.json</c> the
/// TypeScript run updates. An explicit <c>--package-root</c> is used as given.
/// <para>
/// Otherwise the search walks up from the output directory and takes the first
/// directory that holds a <c>package.json</c>, as npm and Bun do. Unlike their walk, it
/// stops at the workspace boundary, so a <c>package.json</c> that belongs to another
/// project further up is never picked (#334). The boundary is the nearest directory,
/// starting at the output directory itself, that is a repository root (it holds
/// <c>.git</c>) or a solution directory (it holds a <c>.sln</c> or <c>.slnx</c> file).
/// Without such a marker, it is the smallest directory that holds both the output
/// directory and the C# project directory, unless that is the filesystem root. Without
/// any boundary, the search covers only the output directory and its parent.
/// </para>
/// <para>
/// When the search finds nothing, the root falls back to the legacy default, the
/// parent of the output directory. When that parent lies above the boundary (the
/// output directory is the boundary itself), the resolution is refused with
/// <see cref="DiagnosticCodes.PackageRootOutsideWorkspace"/>: the caller must pass
/// <c>--package-root</c>.
/// </para>
/// <para>
/// Paths are compared as given after <see cref="Path.GetFullPath(string)"/>: symbolic
/// links are not resolved, and the comparison ignores case on Windows and macOS.
/// </para>
/// </summary>
public static class PackageRootResolver
{
    private const string PackageJsonFileName = "package.json";

    private static readonly string[] SolutionFilePatterns = ["*.sln", "*.slnx"];

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
        var projectDirectory = NormalizeDirectory(
            Path.GetDirectoryName(Path.GetFullPath(projectPath))!
        );
        var boundary = FindWorkspaceBoundary(outputDirectory, projectDirectory);
        var legacyRoot = Path.GetDirectoryName(outputDirectory);

        var nearestPackageJsonDirectory = SelfAndAncestorsUpTo(
                outputDirectory,
                SearchLimit(outputDirectory, boundary, legacyRoot)
            )
            .FirstOrDefault(HasPackageJson);

        if (nearestPackageJsonDirectory is not null)
            return PackageRootResolution.Resolved(nearestPackageJsonDirectory);

        // The legacy fallback must not leave the workspace: refuse when the output
        // directory is the boundary itself (or the filesystem root).
        if (legacyRoot is null || IsSamePath(outputDirectory, boundary))
            return PackageRootResolution.Refused(
                CreateOutsideWorkspaceDiagnostic(outputDirectory, legacyRoot)
            );

        return PackageRootResolution.Resolved(legacyRoot);
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? FindWorkspaceBoundary(string outputDirectory, string projectDirectory)
    {
        var candidates = SelfAndAncestors(outputDirectory).ToList();

        return candidates.FirstOrDefault(IsMarkedWorkspaceRoot)
            ?? candidates.FirstOrDefault(dir =>
                !IsFilesystemRoot(dir) && IsSameOrUnder(projectDirectory, dir)
            );
    }

    /// <summary>
    /// Without a boundary, only the output directory and its parent (the legacy root)
    /// are searched.
    /// </summary>
    private static string SearchLimit(
        string outputDirectory,
        string? boundary,
        string? legacyRoot
    ) => boundary ?? legacyRoot ?? outputDirectory;

    private static bool IsMarkedWorkspaceRoot(string dir) =>
        IsRepositoryRoot(dir) || IsSolutionDirectory(dir);

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
            return SolutionFilePatterns.Any(pattern =>
                Directory.EnumerateFiles(dir, pattern).Any()
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A missing (not yet created) or unreadable directory holds no solution.
            return false;
        }
    }

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

    private static IEnumerable<string> SelfAndAncestorsUpTo(string dir, string limit)
    {
        foreach (var current in SelfAndAncestors(dir))
        {
            yield return current;

            if (IsSamePath(current, limit))
                yield break;
        }
    }

    private static MetanoDiagnostic CreateOutsideWorkspaceDiagnostic(
        string outputDirectory,
        string? legacyRoot
    ) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.PackageRootOutsideWorkspace,
            $"No package.json was found for the output directory '{outputDirectory}' inside "
                + "its workspace, and the default package root (the parent of the output "
                + $"directory, '{legacyRoot ?? "<none>"}') lies outside it: the output "
                + "directory is itself a repository root, a solution directory or the "
                + "directory that holds the C# project. package.json was not written. Pass "
                + "--package-root (MSBuild: MetanoPackageRoot) to choose the package root, or "
                + "--skip-package-json (MetanoSkipPackageJson) to skip the update."
        );
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
