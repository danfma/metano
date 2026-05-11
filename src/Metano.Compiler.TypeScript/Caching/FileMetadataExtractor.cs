using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Caching;

/// <summary>
/// Extracts <see cref="CachedFileMetadata"/> from a freshly
/// transformed <see cref="TsSourceFile"/> and rebuilds a stub
/// <see cref="TsSourceFile"/> from cached metadata on a per-group
/// hit (PR 3c). The stub contains exactly the AST nodes the
/// barrel emitter and cyclic detector look at — every other
/// statement is omitted, which is safe because the cached
/// on-disk content is what actually gets written.
///
/// <para>
/// Imports go through unchanged. Exports collapse to the
/// minimum shape each downstream stage reads:
/// </para>
/// <list type="bullet">
///   <item>Value exports → <see cref="TsConstObject"/> with the
///   same <see cref="TsTopLevel"/>-level name. <c>BarrelFileGenerator</c>
///   matches <c>TsConstObject { Exported: true }</c> in its
///   <c>GetExportedName</c> switch and treats it as a value
///   re-export.</item>
///   <item>Type-only exports → <see cref="TsInterface"/>, which the
///   barrel emitter treats as type-only. The empty
///   property list keeps the stub minimal.</item>
/// </list>
/// </summary>
public static class FileMetadataExtractor
{
    public static CachedFileMetadata Extract(TsSourceFile file)
    {
        var imports = new List<CachedImport>();
        var exports = new List<CachedExport>();

        foreach (var stmt in file.Statements)
        {
            switch (stmt)
            {
                case TsImport import:
                    imports.Add(
                        new CachedImport(
                            import.Names,
                            import.From,
                            import.TypeOnly,
                            import.IsDefault,
                            import.TypeOnlyNames is null
                                ? null
                                : import.TypeOnlyNames.ToArray(),
                            import.IsNamespace,
                            import.Aliases
                        )
                    );
                    break;
                case TsClass { Exported: true } c:
                    exports.Add(new CachedExport(c.Name, TypeOnly: false));
                    break;
                case TsFunction { Exported: true } f:
                    exports.Add(new CachedExport(f.Name, TypeOnly: false));
                    break;
                case TsEnum { Exported: true } e:
                    exports.Add(new CachedExport(e.Name, TypeOnly: false));
                    break;
                case TsConstObject { Exported: true } co:
                    exports.Add(new CachedExport(co.Name, TypeOnly: false));
                    break;
                case TsNamespaceDeclaration { Exported: true } ns:
                    exports.Add(new CachedExport(ns.Name, TypeOnly: false));
                    break;
                case TsTypeAlias { Exported: true } ta:
                    exports.Add(new CachedExport(ta.Name, TypeOnly: true));
                    break;
                case TsInterface { Exported: true } i:
                    exports.Add(new CachedExport(i.Name, TypeOnly: true));
                    break;
            }
        }

        return new CachedFileMetadata(file.FileName, imports, exports);
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
        {
            if (export.TypeOnly)
                statements.Add(new TsInterface(export.Name, []));
            else
                statements.Add(new TsConstObject(export.Name, []));
        }

        return new TsSourceFile(metadata.Path, statements);
    }
}
