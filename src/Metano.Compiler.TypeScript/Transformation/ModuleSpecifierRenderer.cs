using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The single decision point for the text of a module specifier (spec 004
/// Amendment A1). A <see cref="TsPackageModule"/> prints verbatim; a
/// <see cref="TsLocalModule"/> is rendered against the file key of the importing
/// file. Every import and re-export the <see cref="Printer"/> writes, and the order
/// <see cref="ImportCollector"/> sorts local imports in, go through
/// <see cref="Render"/>.
/// </summary>
/// <remarks>
/// Current rendering (pre-FR-011): a module in the importer's directory is
/// <c>./name</c>; a leaf barrel below the importer's directory is its directory
/// (<c>./a/b</c>); any other module goes through the subpath-imports alias
/// (<c>#/a/b/name</c>, or <c>#&lt;alias&gt;/a/b/name</c>).
/// </remarks>
public sealed class ModuleSpecifierRenderer(string? importAlias)
{
    private const string BarrelFileName = "index";

    /// <summary>The renderer of a run with no import alias.</summary>
    public static readonly ModuleSpecifierRenderer Default = new(importAlias: null);

    private readonly string _aliasPrefix = PathNaming.NormalizeImportAliasPrefix(importAlias);

    /// <summary>The specifier text for <paramref name="module"/> imported from <paramref name="importerFileKey"/>.</summary>
    public string Render(TsModuleSpecifier module, string importerFileKey) =>
        module switch
        {
            TsPackageModule package => package.Specifier,
            TsLocalModule local => RenderLocal(local.FileKey, importerFileKey),
            _ => throw new InvalidOperationException(
                $"Unhandled module specifier '{module.GetType().Name}'."
            ),
        };

    private string RenderLocal(string targetKey, string importerFileKey)
    {
        var importerDirectory = DirectoryOf(importerFileKey);
        var targetDirectory = DirectoryOf(targetKey);

        if (targetDirectory == importerDirectory)
            return "./" + FileNameOf(targetKey);

        if (IsBarrelBelow(targetKey, targetDirectory, importerDirectory))
            return "./" + RelativeTo(importerDirectory, targetDirectory);

        return _aliasPrefix + "/" + targetKey;
    }

    private static bool IsBarrelBelow(
        string targetKey,
        string targetDirectory,
        string importerDirectory
    ) =>
        FileNameOf(targetKey) == BarrelFileName
        && (
            importerDirectory.Length == 0
            || targetDirectory.StartsWith(importerDirectory + "/", StringComparison.Ordinal)
        );

    private static string RelativeTo(string directory, string descendant) =>
        directory.Length == 0 ? descendant : descendant[(directory.Length + 1)..];

    private static string DirectoryOf(string fileKey)
    {
        var slash = fileKey.LastIndexOf('/');

        return slash < 0 ? "" : fileKey[..slash];
    }

    private static string FileNameOf(string fileKey) => fileKey[(fileKey.LastIndexOf('/') + 1)..];
}
