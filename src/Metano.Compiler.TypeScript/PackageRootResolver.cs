using System.Diagnostics.CodeAnalysis;
using Metano.Compiler.Diagnostics;

namespace Metano;

/// <summary>
/// Finds the consumer package root (the directory whose <c>package.json</c> the
/// TypeScript run updates) when <c>--package-root</c> is not given.
/// <para>
/// The search walks up from the output directory and takes the first directory that
/// holds a <c>package.json</c>, as npm and Bun do. Unlike their walk, it stops at the
/// workspace boundary: the nearest directory, starting at the output directory itself,
/// that is the C# project directory, a repository root (it holds <c>.git</c>) or a
/// solution directory (it holds a <c>.sln</c> or <c>.slnx</c> file). A
/// <c>package.json</c> above the boundary belongs to another project and is never
/// picked (#334). Without any boundary, the search covers only the output directory
/// and its parent.
/// </para>
/// <para>
/// When the search finds nothing, the root falls back to the legacy default, the
/// parent of the output directory. When that parent lies above the boundary (the
/// output directory is the boundary itself), the resolution is refused with
/// <see cref="DiagnosticCodes.PackageRootOutsideWorkspace"/>: the caller must pass
/// <c>--package-root</c>.
/// </para>
/// </summary>
public static class PackageRootResolver
{
    private const string PackageJsonFileName = "package.json";

    private static readonly string[] SolutionFilePatterns = ["*.sln", "*.slnx"];

    /// <summary>
    /// Resolves the package root for <paramref name="outputDir"/>, bounded by the
    /// workspace that holds it. <paramref name="projectDir"/> is the directory of the
    /// C# project file.
    /// </summary>
    public static PackageRootResolution Resolve(string outputDir, string projectDir)
    {
        var output = NormalizeDirectory(outputDir);
        var project = NormalizeDirectory(projectDir);
        var legacyRoot = Path.GetDirectoryName(output);
        var boundary = SelfAndAncestors(output).FirstOrDefault(dir => IsBoundary(dir, project));
        var searchLimit = boundary ?? legacyRoot ?? output;

        var withPackageJson = SelfAndAncestorsUpTo(output, searchLimit)
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, PackageJsonFileName)));

        if (withPackageJson is not null)
            return PackageRootResolution.Resolved(withPackageJson);

        if (legacyRoot is null || boundary == output)
            return PackageRootResolution.Refused(OutsideWorkspace(output, legacyRoot));

        return PackageRootResolution.Resolved(legacyRoot);
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsBoundary(string dir, string projectDir) =>
        string.Equals(dir, projectDir, StringComparison.Ordinal)
        || IsRepositoryRoot(dir)
        || IsSolutionDirectory(dir);

    private static bool IsRepositoryRoot(string dir)
    {
        // A worktree or submodule stores `.git` as a file, a main checkout as a folder.
        var gitPath = Path.Combine(dir, ".git");

        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    private static bool IsSolutionDirectory(string dir) =>
        Directory.Exists(dir)
        && SolutionFilePatterns.Any(pattern => Directory.EnumerateFiles(dir, pattern).Any());

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

            if (string.Equals(current, limit, StringComparison.Ordinal))
                yield break;
        }
    }

    private static MetanoDiagnostic OutsideWorkspace(string outputDir, string? legacyRoot) =>
        new(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.PackageRootOutsideWorkspace,
            $"No package.json was found for the output directory '{outputDir}' inside its "
                + "workspace, and the default package root (the parent of the output "
                + $"directory, '{legacyRoot ?? "<none>"}') lies outside the C# project, "
                + "repository or solution that holds it. package.json was not written. "
                + "Pass --package-root (MSBuild: MetanoPackageRoot) to choose the package "
                + "root, or --skip-package-json (MetanoSkipPackageJson) to skip the update."
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
