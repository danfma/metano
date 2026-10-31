using Metano.Compiler;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// A namespace barrel and the directory it is exported under in
/// <c>package.json#exports</c> (spec 008).
/// </summary>
/// <param name="FileName">The barrel's path relative to the output root
/// (<c>video-studio/domain/episodes/index.ts</c>).</param>
/// <param name="Directory">The barrel's on-disk directory (<c>video-studio/domain/episodes</c>;
/// <c>""</c> for the output root).</param>
/// <param name="ExportedDirectory">The directory with the package's subpath root stripped
/// (<c>episodes</c>; <c>""</c> for the package root).</param>
public sealed record ExportedBarrel(string FileName, string Directory, string ExportedDirectory);

/// <summary>
/// Two barrels that map to the same exported directory. <see cref="Kept"/> stays in
/// <c>package.json#exports</c>; <see cref="Dropped"/> is left out.
/// </summary>
public sealed record ExportKeyCollision(
    string ExportedDirectory,
    ExportedBarrel Kept,
    ExportedBarrel Dropped
)
{
    /// <summary>The contested key as written without an output prefix (<c>.</c>, <c>./x</c>).</summary>
    public string Key => ExportedDirectory.Length == 0 ? "." : $"./{ExportedDirectory}";
}

/// <summary>
/// The exported barrels (one per exports key, in ordinal file order) plus every key
/// collision found while assigning them.
/// </summary>
public sealed record ExportSubpathResolution(
    IReadOnlyList<ExportedBarrel> Barrels,
    IReadOnlyList<ExportKeyCollision> Collisions
);

/// <summary>
/// The single rule mapping namespace paths to a package's public subpaths under a
/// <see cref="PackageSubpathRoot"/> (spec 008). The producer's <c>package.json#exports</c>
/// keys, the transform-time collision check and every consumer's cross-package specifier
/// all go through it, so they agree. On-disk paths never do.
/// </summary>
public static class PackageExportSubpaths
{
    /// <summary>
    /// Maps a kebab namespace path to its exported subpath: the root itself →
    /// <c>""</c> (package root), a path under the root → the remainder, anything
    /// else → unchanged.
    /// </summary>
    public static string Strip(PackageSubpathRoot root, string namespacePath)
    {
        if (!IsUnder(root, namespacePath))
            return namespacePath;

        var rootPath = PathNaming.ToKebabPath(root.Namespace);

        return namespacePath.Length == rootPath.Length
            ? ""
            : namespacePath[(rootPath.Length + 1)..];
    }

    /// <summary>
    /// True when <paramref name="namespacePath"/> is the root or lies under it on a
    /// segment boundary (<c>video-studio/domain-events</c> is NOT under
    /// <c>video-studio/domain</c>). Always false for <see cref="PackageSubpathRoot.None"/>.
    /// </summary>
    public static bool IsUnder(PackageSubpathRoot root, string namespacePath)
    {
        if (root.IsNone)
            return false;

        var rootPath = PathNaming.ToKebabPath(root.Namespace);

        return namespacePath == rootPath
            || namespacePath.StartsWith(rootPath + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Assigns an exported directory to every namespace barrel (<c>index.ts</c>) in
    /// <paramref name="fileNames"/>. When two barrels claim one directory, the barrel
    /// under the root keeps it (its consumers' specifiers point there) and the
    /// collision is recorded.
    /// </summary>
    public static ExportSubpathResolution Resolve(
        IEnumerable<string> fileNames,
        PackageSubpathRoot root
    )
    {
        var claims = new Dictionary<string, ExportedBarrel>(StringComparer.Ordinal);
        var collisions = new List<ExportKeyCollision>();

        var barrels = fileNames
            .Select(NormalizePath)
            .Where(name => Path.GetFileName(name).Equals("index.ts", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        foreach (var fileName in barrels)
        {
            var directory = Path.GetDirectoryName(fileName)?.Replace('\\', '/') ?? "";
            var barrel = new ExportedBarrel(fileName, directory, Strip(root, directory));

            ClaimExportKey(claims, collisions, barrel, root);
        }

        return new ExportSubpathResolution([.. claims.Values], collisions);
    }

    private static void ClaimExportKey(
        Dictionary<string, ExportedBarrel> claims,
        List<ExportKeyCollision> collisions,
        ExportedBarrel barrel,
        PackageSubpathRoot root
    )
    {
        if (!claims.TryGetValue(barrel.ExportedDirectory, out var claimant))
        {
            claims[barrel.ExportedDirectory] = barrel;

            return;
        }

        var underRootBarrelWins = IsUnder(root, barrel.Directory);
        var (kept, dropped) = underRootBarrelWins ? (barrel, claimant) : (claimant, barrel);

        claims[barrel.ExportedDirectory] = kept;
        collisions.Add(new ExportKeyCollision(barrel.ExportedDirectory, kept, dropped));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('.', '/');
}
