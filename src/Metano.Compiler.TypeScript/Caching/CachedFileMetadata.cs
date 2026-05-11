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
/// Exports keep only the name + a coarse type-vs-value flag — that
/// is the only distinction the barrel emitter cares about.
/// </para>
/// </summary>
public sealed record CachedFileMetadata(
    string Path,
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

public sealed record CachedExport(string Name, bool TypeOnly);
