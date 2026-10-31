using Metano.Compiler;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The package's declared <c>[EmitPackage(SubpathRoot = …)]</c>, kept as the kebab path
/// of its namespace (<c>VideoStudio.Domain</c> → <c>video-studio/domain</c>). Strips that
/// prefix from a namespace path on whole segments, so the producer's
/// <c>package.json#exports</c> keys and every consumer's cross-package specifier apply
/// the exact same mapping (spec 008). On-disk paths never go through this type.
/// </summary>
public sealed class ExportsSubpathRoot
{
    private ExportsSubpathRoot(string kebabPath) => KebabPath = kebabPath;

    /// <summary>No root declared: every path is returned unchanged.</summary>
    public static ExportsSubpathRoot None { get; } = new("");

    private string KebabPath { get; }

    /// <summary>
    /// Builds the root from a validated namespace (see
    /// <see cref="SymbolHelper.EmitPackageInfo.SubpathRoot"/>); <c>null</c> or empty
    /// yields <see cref="None"/>.
    /// </summary>
    public static ExportsSubpathRoot From(string? rootNamespace) =>
        string.IsNullOrEmpty(rootNamespace)
            ? None
            : new ExportsSubpathRoot(
                string.Join("/", rootNamespace.Split('.').Select(SymbolHelper.ToKebabCase))
            );

    /// <summary>
    /// Maps a kebab namespace path (no leading <c>./</c>) to its exported subpath:
    /// the root itself → <c>""</c> (package root), a path under the root → the
    /// remainder, anything else → unchanged.
    /// </summary>
    public string Apply(string namespacePath)
    {
        if (!Contains(namespacePath))
            return namespacePath;

        return namespacePath.Length == KebabPath.Length
            ? ""
            : namespacePath[(KebabPath.Length + 1)..];
    }

    /// <summary>
    /// True when <paramref name="namespacePath"/> is the root or lies under it on a
    /// segment boundary (<c>video-studio/domain-events</c> is NOT under
    /// <c>video-studio/domain</c>).
    /// </summary>
    public bool Contains(string namespacePath) =>
        KebabPath.Length > 0
        && (
            namespacePath == KebabPath
            || namespacePath.StartsWith(KebabPath + "/", StringComparison.Ordinal)
        );
}
