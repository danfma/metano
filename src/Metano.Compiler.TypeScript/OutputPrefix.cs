using Metano.Compiler.Diagnostics;

namespace Metano;

/// <summary>
/// Computes the path segment from a package's source root to the
/// transpiler's output directory. Used by <see cref="PackageJsonWriter"/>
/// to align <c>imports</c> / <c>exports</c> subpaths with the build
/// tool's <c>rootDir</c> when types are emitted under a subdirectory
/// (e.g., <c>src/domain</c>).
/// </summary>
internal static class OutputPrefix
{
    /// <summary>
    /// Resolves the output prefix from a <paramref name="srcRoot"/>
    /// hint (the consumer's <c>--src-root</c> flag — may be null,
    /// empty, <c>.</c>, or a path fragment) and the
    /// <paramref name="srcRelative"/> path of the output directory
    /// relative to the package root. Returns the prefix and an
    /// optional <see cref="MetanoDiagnostic"/> warning when the hint
    /// points outside the output path.
    /// </summary>
    public static (string Prefix, MetanoDiagnostic? Diagnostic) Resolve(
        string? srcRoot,
        string srcRelative
    )
    {
        var resolvedSrcRoot = ResolveSrcRoot(srcRoot, srcRelative);

        if (resolvedSrcRoot.Length == 0)
            return (srcRelative, null);
        if (srcRelative == resolvedSrcRoot)
            return ("", null);
        if (srcRelative.StartsWith(resolvedSrcRoot + "/", StringComparison.Ordinal))
            return (srcRelative[(resolvedSrcRoot.Length + 1)..], null);

        return (
            "",
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.CrossPackageResolution,
                $"--src-root '{resolvedSrcRoot}' is not an ancestor of output path "
                    + $"'{srcRelative}'. Ignoring prefix — imports/exports paths may be incorrect. "
                    + $"Set --src-root to the directory that your build tool uses as rootDir."
            )
        );
    }

    /// <summary>
    /// Normalises the <c>--src-root</c> hint. <c>null</c> / empty
    /// infers from the first segment of <paramref name="srcRelative"/>;
    /// <c>.</c> means the package root itself is the source root
    /// (return empty so the full <paramref name="srcRelative"/>
    /// becomes the prefix).
    /// </summary>
    private static string ResolveSrcRoot(string? srcRoot, string srcRelative)
    {
        var raw = srcRoot?.Replace('\\', '/').TrimStart('/').TrimEnd('/');
        if (raw is null or "")
            return srcRelative.Split('/')[0];
        if (raw == ".")
            return "";
        return raw.TrimStart('.', '/');
    }
}
