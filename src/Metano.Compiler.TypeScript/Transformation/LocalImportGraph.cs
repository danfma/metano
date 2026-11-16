using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The directed graph of local imports between generated files: each file
/// (keyed by its relative path without the <c>.ts</c> extension) points at
/// every file it imports through the local package alias (<c>#</c>, or the
/// configured <c>--import-alias</c>, and its <c>/</c> subpaths) or a relative
/// path. An <c>index.ts</c> also answers to its directory's barrel path.
/// Imports from external packages are not edges. The value graph keeps only
/// the imports an ES module evaluates (every import but <c>import type</c>),
/// the edges that decide the order modules load in.
/// </summary>
internal sealed class LocalImportGraph
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _edges;
    private readonly IReadOnlyDictionary<string, string> _byImportKey;
    private readonly string _aliasPrefix;

    private LocalImportGraph(
        IReadOnlyDictionary<string, IReadOnlyList<string>> edges,
        IReadOnlyDictionary<string, string> byImportKey,
        string aliasPrefix
    )
    {
        _edges = edges;
        _byImportKey = byImportKey;
        _aliasPrefix = aliasPrefix;
    }

    /// <summary>The import alias prefix the graph resolves (<c>#</c> by default).</summary>
    public string AliasPrefix => _aliasPrefix;

    /// <summary>The keys of every file in the graph.</summary>
    public IEnumerable<string> Files => _edges.Keys;

    /// <summary>Every import of <paramref name="files"/>, type-only ones included.</summary>
    public static LocalImportGraph AllImports(
        IReadOnlyList<TsSourceFile> files,
        string? importAlias
    ) => Build(files, importAlias, _ => true);

    /// <summary>The imports of <paramref name="files"/> an ES module evaluates.</summary>
    public static LocalImportGraph ValueImports(
        IReadOnlyList<TsSourceFile> files,
        string? importAlias
    ) => Build(files, importAlias, import => !import.TypeOnly);

    /// <summary>The key of <paramref name="file"/>: its relative path without the <c>.ts</c> extension.</summary>
    public static string KeyOf(TsSourceFile file) =>
        file.FileName.EndsWith(".ts", StringComparison.Ordinal)
            ? file.FileName[..^3]
            : file.FileName;

    /// <summary>The files <paramref name="file"/> imports directly.</summary>
    public IReadOnlyList<string> TargetsOf(string file) =>
        _edges.TryGetValue(file, out var targets) ? targets : [];

    /// <summary>Whether a chain of imports leads from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool Reaches(string from, string to)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { from };
        var pending = new Stack<string>([from]);

        while (pending.TryPop(out var current))
        {
            if (current == to)
                return true;

            foreach (var next in TargetsOf(current).Where(visited.Add))
                pending.Push(next);
        }

        return false;
    }

    /// <summary>
    /// The file that <paramref name="importer"/> imports <paramref name="name"/>
    /// from as a value; <c>null</c> when no local value import binds it.
    /// </summary>
    public string? ValueSourceOf(TsSourceFile importer, string name) =>
        importer
            .Statements.OfType<TsImport>()
            .Where(import => !import.TypeOnly && ValueBindingsOf(import).Contains(name))
            .Select(import => Resolve(import.From, KeyOf(importer), _byImportKey, _aliasPrefix))
            .FirstOrDefault(key => key is not null);

    /// <summary>The local names <paramref name="import"/> binds to values (an alias under its local name).</summary>
    private static IEnumerable<string> ValueBindingsOf(TsImport import)
    {
        var named = import
            .Names.Where(name => import.TypeOnlyNames?.Contains(name) != true)
            .Select(name =>
                import.Aliases is { } aliases && aliases.TryGetValue(name, out var local)
                    ? local
                    : name
            );

        var extra = (import.ExtraAliases ?? [])
            .Where(alias => !alias.TypeOnly)
            .Select(alias => alias.Local);

        return named.Concat(extra);
    }

    private static LocalImportGraph Build(
        IReadOnlyList<TsSourceFile> files,
        string? importAlias,
        Func<TsImport, bool> isEdge
    )
    {
        var aliasPrefix = PathNaming.NormalizeImportAliasPrefix(importAlias);
        var byImportKey = ImportKeys(files);

        // Indexer writes, not ToDictionary: two files of one name (an MS0008
        // collision) keep the last, as the diagnostics that report it expect.
        var edges = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var importer = KeyOf(file);

            edges[importer] = file
                .Statements.OfType<TsImport>()
                .Where(isEdge)
                .Select(import => Resolve(import.From, importer, byImportKey, aliasPrefix))
                .OfType<string>()
                .ToList();
        }

        return new LocalImportGraph(edges, byImportKey, aliasPrefix);
    }

    /// <summary>
    /// Every key a local import may name, mapped to its file: the file's own
    /// key, and for an <c>index</c> file its directory (<c>""</c> for the root
    /// <c>index.ts</c>).
    /// </summary>
    private static Dictionary<string, string> ImportKeys(IReadOnlyList<TsSourceFile> files)
    {
        var byImportKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in files.Select(KeyOf))
        {
            byImportKey[key] = key;

            if (Path.GetFileName(key).Equals("index", StringComparison.Ordinal))
                byImportKey[Path.GetDirectoryName(key)?.Replace('\\', '/') ?? ""] = key;
        }

        return byImportKey;
    }

    private static string? Resolve(
        string from,
        string importerKey,
        IReadOnlyDictionary<string, string> byImportKey,
        string aliasPrefix
    ) =>
        LocalImportKey(from, importerKey, aliasPrefix) is { } key
        && byImportKey.TryGetValue(key, out var file)
            ? file
            : null;

    /// <summary>
    /// The key a local import specifier names: the path under the alias, or a
    /// relative path resolved against the importer's directory. <c>null</c>
    /// for an external package.
    /// </summary>
    private static string? LocalImportKey(string from, string importerKey, string aliasPrefix)
    {
        if (from == aliasPrefix)
            return "";

        var aliasSubpathPrefix = aliasPrefix + "/";

        if (from.StartsWith(aliasSubpathPrefix, StringComparison.Ordinal))
            return from[aliasSubpathPrefix.Length..];

        if (!from.StartsWith("./", StringComparison.Ordinal))
            return null;

        var directory = Path.GetDirectoryName(importerKey)?.Replace('\\', '/') ?? "";
        var relative = from[2..];

        return directory.Length > 0 ? directory + "/" + relative : relative;
    }
}
