using System.Security.Cryptography;
using System.Text;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Caching;

/// <summary>
/// Extracts <see cref="CachedFileMetadata"/> from a freshly
/// transformed <see cref="TsSourceFile"/> and rebuilds a stub
/// <see cref="TsSourceFile"/> from cached metadata on a per-group
/// cache hit. The stub contains exactly the AST nodes the
/// barrel emitter and cyclic detector look at — every other
/// statement is omitted, which is safe because the cached
/// on-disk content is what actually gets written.
///
/// <para>
/// Imports go through unchanged. Exports preserve the original
/// concrete <see cref="TsTopLevel"/> shape via
/// <see cref="CachedExportKind"/> so a cache rehydration produces
/// the exact same node type the fresh AST would (a
/// <c>TsTypeAlias</c> rebuilds as <c>TsTypeAlias</c>, not as a
/// generic type-only stub). This guards against silent
/// misclassification when a downstream stage adds a check that
/// distinguishes one type-only shape from another.
/// </para>
/// <para>
/// Non-exported and side-effect-only shapes
/// (<see cref="TsTopLevelStatement"/>, <see cref="TsReExport"/>,
/// <see cref="TsModuleExport"/>, <see cref="TsExportImportAlias"/>)
/// participate via the file's on-disk content but contribute nothing
/// to the barrel's export surface; the switch acknowledges them
/// explicitly so a future addition to <see cref="TsTopLevel"/> has
/// to land here too (the <c>default</c> arm throws).
/// </para>
/// </summary>
public static class FileMetadataExtractor
{
    public static CachedFileMetadata Extract(TsSourceFile file, string content)
    {
        var imports = new List<CachedImport>();
        var exports = new List<CachedExport>();

        foreach (var stmt in file.Statements)
        {
            if (stmt is TsImport import)
            {
                imports.Add(CaptureImport(import));
                continue;
            }

            if (TryClassifyExport(stmt) is { } export)
            {
                exports.Add(export);
                continue;
            }

            RequireKnownNonExportShape(stmt);
        }

        return new CachedFileMetadata(file.FileName, Sha256(content), imports, exports);
    }

    private static CachedImport CaptureImport(TsImport import) =>
        new(
            import.Names,
            import.From,
            import.TypeOnly,
            import.IsDefault,
            import.TypeOnlyNames is null ? null : import.TypeOnlyNames.ToArray(),
            import.IsNamespace,
            import.Aliases
        );

    /// <summary>
    /// Maps each exported <c>TsTopLevel</c> subtype to its
    /// <see cref="CachedExport"/> entry. Returns <see langword="null"/>
    /// for non-exported variants and for shapes that contribute
    /// nothing to the barrel surface — the caller treats both cases
    /// as "not an export" via <see cref="RequireKnownNonExportShape"/>.
    /// </summary>
    private static CachedExport? TryClassifyExport(TsTopLevel stmt) =>
        stmt switch
        {
            TsClass c when c.Exported => new CachedExport(c.Name, CachedExportKind.Class),
            TsFunction f when f.Exported => new CachedExport(f.Name, CachedExportKind.Function),
            TsEnum e when e.Exported => new CachedExport(e.Name, CachedExportKind.Enum),
            TsConstObject co when co.Exported => new CachedExport(
                co.Name,
                CachedExportKind.ConstObject
            ),
            TsNamespaceDeclaration ns when ns.Exported => new CachedExport(
                ns.Name,
                CachedExportKind.Namespace
            ),
            TsTypeAlias ta when ta.Exported => new CachedExport(
                ta.Name,
                CachedExportKind.TypeAlias
            ),
            TsInterface i when i.Exported => new CachedExport(i.Name, CachedExportKind.Interface),
            _ => null,
        };

    /// <summary>
    /// Enforces that a non-export statement belongs to one of the
    /// shapes the cache deliberately ignores. Splitting the catch-all
    /// into two named groups keeps the intent explicit:
    /// <list type="bullet">
    ///   <item>Non-exported variants of the seven exportable shapes —
    ///   contribute on-disk content only.</item>
    ///   <item>Side-effect / re-export / alias shapes — barrel emitter
    ///   ignores them by design.</item>
    /// </list>
    /// Anything else throws so a future <c>TsTopLevel</c> subtype
    /// cannot slip in unnoticed and silently miss the cache.
    /// </summary>
    private static void RequireKnownNonExportShape(TsTopLevel stmt)
    {
        switch (stmt)
        {
            // Non-exported variants — body still ships on disk.
            case TsClass:
            case TsFunction:
            case TsEnum:
            case TsConstObject:
            case TsNamespaceDeclaration:
            case TsTypeAlias:
            case TsInterface:
                return;

            // Side-effect / re-export / alias shapes — the barrel
            // emitter does not classify them as exportable surface.
            case TsTopLevelStatement:
            case TsReExport:
            case TsModuleExport:
            case TsExportImportAlias:
                return;

            default:
                throw new InvalidOperationException(
                    $"Unhandled TsTopLevel subtype '{stmt.GetType().Name}' in "
                        + "FileMetadataExtractor. Add an explicit case so cache "
                        + "rehydration stays consistent with the fresh AST."
                );
        }
    }

    /// <summary>
    /// Hex SHA-256 of UTF-8 <paramref name="content"/>. Used by the
    /// per-group cache to fingerprint each emitted file so a stale
    /// entry whose on-disk content has been edited gets rejected.
    /// </summary>
    public static string Sha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static TsSourceFile BuildStub(CachedFileMetadata metadata)
    {
        var statements = new List<TsTopLevel>(metadata.Imports.Count + metadata.Exports.Count);

        foreach (var import in metadata.Imports)
        {
            statements.Add(
                new TsImport(
                    import.Names.ToArray(),
                    import.From,
                    import.TypeOnly,
                    import.IsDefault,
                    import.TypeOnlyNames is null
                        ? null
                        : new HashSet<string>(import.TypeOnlyNames, StringComparer.Ordinal),
                    import.IsNamespace,
                    import.Aliases
                )
            );
        }

        foreach (var export in metadata.Exports)
            statements.Add(BuildExportStub(export));

        return new TsSourceFile(metadata.Path, statements);
    }

    /// <summary>
    /// Reconstructs a minimal <see cref="TsTopLevel"/> node of the
    /// exact original kind. Empty bodies / parameter lists are fine
    /// because the cached on-disk file is what actually ships;
    /// downstream stages only consume the node's type and exported
    /// name.
    /// </summary>
    private static TsTopLevel BuildExportStub(CachedExport export) =>
        export.Kind switch
        {
            CachedExportKind.Class => new TsClass(export.Name, Constructor: null, Members: []),
            CachedExportKind.Function => new TsFunction(
                export.Name,
                Parameters: [],
                ReturnType: new TsVoidType(),
                Body: []
            ),
            CachedExportKind.Enum => new TsEnum(export.Name, Members: []),
            CachedExportKind.ConstObject => new TsConstObject(export.Name, Entries: []),
            CachedExportKind.Namespace => new TsNamespaceDeclaration(export.Name, Functions: []),
            CachedExportKind.TypeAlias => new TsTypeAlias(export.Name, Type: new TsAnyType()),
            CachedExportKind.Interface => new TsInterface(export.Name, Properties: []),
            _ => throw new InvalidOperationException(
                $"Unhandled CachedExportKind '{export.Kind}' in BuildExportStub. "
                    + "Add a switch arm matching the new enum value."
            ),
        };

    /// <summary>
    /// Rejects rooted paths and any segment equal to <c>..</c> so a
    /// hand-edited <c>.metano-cache-groups-typescript.json</c> cannot
    /// redirect <c>File.ReadAllText</c> to an arbitrary disk
    /// location (mirrors the safety check the host-level cache from
    /// host-level cache applies in <c>CacheKeyBuilder</c>).
    /// </summary>
    public static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;
        if (Path.IsPathRooted(relativePath))
            return false;
        foreach (var segment in relativePath.Split('/', '\\'))
        {
            if (segment == "..")
                return false;
        }
        return true;
    }
}
