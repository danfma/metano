namespace Metano.Compiler.TypeScript.Caching;

/// <summary>
/// Per-cached-file shape persisted alongside the per-group closure
/// hash (ADR-0023 follow-up / PR 3c). On a per-group cache hit the
/// host reads the file's content from disk and rebuilds a minimal
/// <see cref="Metano.Compiler.TypeScript.AST.TsSourceFile"/> from
/// this metadata so the downstream stages
/// (<c>BarrelFileGenerator</c>, <c>CyclicReferenceDetector</c>) can
/// walk it identically to a fresh AST.
///
/// <para>
/// Imports preserve every detail of <c>TsImport</c> because both
/// downstream consumers read the full shape (typeOnly,
/// per-name typeOnlyNames, default form, namespace form, aliases).
/// Exports carry a <see cref="CachedExportKind"/> discriminator so a
/// stub rebuild produces the same concrete AST shape (<c>TsClass</c>
/// vs <c>TsConstObject</c> vs <c>TsInterface</c> vs <c>TsTypeAlias</c>,
/// …). The cache must keep enough state to round-trip the exact
/// classification any downstream stage applies — otherwise a stage
/// that distinguishes <c>TsTypeAlias</c> from <c>TsInterface</c>
/// would silently see the wrong shape on a cache hit.
/// </para>
/// </summary>
public sealed record CachedFileMetadata(
    string Path,
    string ContentHash,
    IReadOnlyList<CachedImport> Imports,
    IReadOnlyList<CachedExport> Exports
);

public sealed record CachedImport(
    IReadOnlyList<string> Names,
    string From,
    bool TypeOnly = false,
    bool IsDefault = false,
    IReadOnlyList<string>? TypeOnlyNames = null,
    bool IsNamespace = false,
    IReadOnlyDictionary<string, string>? Aliases = null
);

public sealed record CachedExport(string Name, CachedExportKind Kind);

/// <summary>
/// Discriminator for the concrete <c>TsTopLevel</c> shape an export
/// originated from. Used on cache-rehydration to rebuild the same
/// shape so <c>BarrelFileGenerator.GetExportedName</c> and
/// <c>IsTypeOnlyExport</c> classify the stub identically to the
/// fresh AST. Adding a new exported <c>TsTopLevel</c> subtype must
/// land here and in <c>FileMetadataExtractor</c>'s switch — the
/// reflection-based <c>Extract_CoversEveryTsTopLevelSubtype</c> test
/// and the runtime <c>default</c> arm both fail otherwise.
/// <para>
/// The on-disk schema persists each value by name (see
/// <c>GroupCacheFile</c>'s <c>JsonStringEnumConverter</c>), so the
/// enum can be reordered without invalidating caches. New values
/// can be appended at any position.
/// </para>
/// </summary>
public enum CachedExportKind
{
    Class,
    Function,
    Enum,
    ConstObject,
    Namespace,
    TypeAlias,
    Interface,
}
