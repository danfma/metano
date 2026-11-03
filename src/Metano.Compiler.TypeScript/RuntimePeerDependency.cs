using System.Reflection;
using System.Text.RegularExpressions;
using Metano.Compiler;

namespace Metano;

/// <summary>
/// The <c>metano-runtime</c> peer dependency of a generated package: when any emitted file
/// imports the runtime, the package declares it in <c>peerDependencies</c> so the consuming
/// app supplies a single shared copy (the runtime's classes rely on <c>instanceof</c>).
/// </summary>
/// <remarks>
/// <c>metano-runtime</c> is published with the same version as the compiler release
/// (<c>.releaserc.json</c> packs the compiler with <c>-p:Version=X</c> and
/// <c>scripts/sync-npm-version.sh</c> publishes the runtime as <c>X</c> from the same
/// tag), so the compiler's own informational version is the runtime version it targets.
/// </remarks>
public static partial class RuntimePeerDependency
{
    public const string PackageName = "metano-runtime";

    /// <summary>
    /// Specifier for builds without a published version (local MinVer builds carry a
    /// prerelease or <c>0.0.0</c> version no registry has): any runtime satisfies it.
    /// </summary>
    public const string UnpublishedBuildSpecifier = "*";

    /// <summary>The specifier for the running compiler's own release version.</summary>
    public static string CurrentSpecifier { get; } = SpecifierFor(ReadCompilerVersion());

    /// <summary>
    /// <c>^Major.Minor.Patch</c> for a stable release version (build metadata such as the
    /// <c>+sha</c> suffix is ignored); <see cref="UnpublishedBuildSpecifier"/> for a
    /// prerelease, <c>0.0.0</c>, missing or unparseable version.
    /// </summary>
    public static string SpecifierFor(string? informationalVersion)
    {
        var match = StableVersion().Match(informationalVersion ?? "");

        if (!match.Success || match.Groups["version"].Value == "0.0.0")
            return UnpublishedBuildSpecifier;

        return $"^{match.Groups["version"].Value}";
    }

    /// <summary>
    /// True when any generated file imports <c>metano-runtime</c> (or one of its subpaths).
    /// Reads the printed contents rather than the AST: files reused from the incremental
    /// cache are statement-less stubs, but their content is always available.
    /// </summary>
    public static bool IsImportedBy(IEnumerable<GeneratedFile> files) =>
        files.Any(file => RuntimeImport().IsMatch(file.Content));

    private static string? ReadCompilerVersion() =>
        typeof(RuntimePeerDependency)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

    [GeneratedRegex(
        """^import\b[^\n]*\bfrom "metano-runtime(?:/[^"\n]*)?";\s*$""",
        RegexOptions.Multiline
    )]
    private static partial Regex RuntimeImport();

    [GeneratedRegex(@"^(?<version>\d+\.\d+\.\d+)(\+.*)?$")]
    private static partial Regex StableVersion();
}
