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
/// (<see cref="CompilerRelease"/>): <c>scripts/sync-npm-version.sh</c> publishes the runtime
/// as <c>X</c> from the same tag the compiler is packed from. So the compiler's own release
/// version is the runtime version it targets.
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
    public static string CurrentSpecifier { get; } = SpecifierFor(CompilerRelease.Version);

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
    /// True when <paramref name="specifier"/> has the shape this class writes
    /// (<c>*</c> or <c>^Major.Minor.Patch</c>), so a stored value of that shape is the
    /// writer's own and may be refreshed; any other shape is the consumer's.
    /// </summary>
    public static bool IsOwnSpecifier(string? specifier) =>
        specifier == UnpublishedBuildSpecifier
        || (specifier is not null && StableCaretSpecifier().IsMatch(specifier));

    /// <summary>
    /// <see cref="CurrentSpecifier"/> when any generated file imports the runtime;
    /// <c>null</c> (no peer dependency) otherwise.
    /// </summary>
    public static string? PeerSpecifierFor(IEnumerable<GeneratedFile> files) =>
        IsImportedBy(files) ? CurrentSpecifier : null;

    /// <summary>
    /// True when any generated file imports <c>metano-runtime</c> (or one of its subpaths).
    /// Reads the printed contents because they are the one input available on every path:
    /// cold, per-group cache hit and full cache hit (only the full-hit path has
    /// statement-less <c>TsSourceFile</c> stubs, so the AST cannot be walked there). It relies
    /// on the printer's single-line <c>import … from "…";</c> format
    /// (<c>Printer.PrintImport</c>); the compiler emits no re-export of the runtime.
    /// </summary>
    public static bool IsImportedBy(IEnumerable<GeneratedFile> files) =>
        files.Any(file => RuntimeImport().IsMatch(file.Content));

    [GeneratedRegex(
        """^import\b[^\n]*\bfrom "metano-runtime(?:/[^"\n]*)?";\s*$""",
        RegexOptions.Multiline
    )]
    private static partial Regex RuntimeImport();

    [GeneratedRegex(@"^(?<version>\d+\.\d+\.\d+)(\+.*)?$")]
    private static partial Regex StableVersion();

    [GeneratedRegex(@"^\^\d+\.\d+\.\d+$")]
    private static partial Regex StableCaretSpecifier();
}
