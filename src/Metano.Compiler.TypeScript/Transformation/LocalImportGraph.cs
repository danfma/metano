using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The directed graph of local imports between generated files: each file
/// (keyed by its relative path without the <c>.ts</c> / <c>.tsx</c> extension) points at
/// every generated file it imports (<see cref="TsLocalModule"/>, whose file
/// key names the target directly; a namespace barrel is its <c>index</c> file).
/// Imports from external packages are not edges. The value graph keeps only
/// the imports an ES module evaluates (every import but <c>import type</c>),
/// the edges that decide the order modules load in.
/// </summary>
internal sealed class LocalImportGraph
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _edges;
    private readonly IReadOnlySet<string> _fileKeys;

    private LocalImportGraph(
        IReadOnlyDictionary<string, IReadOnlyList<string>> edges,
        IReadOnlySet<string> fileKeys
    )
    {
        _edges = edges;
        _fileKeys = fileKeys;
    }

    /// <summary>The keys of every file in the graph.</summary>
    public IEnumerable<string> Files => _edges.Keys;

    /// <summary>Every import of <paramref name="files"/>, type-only ones included.</summary>
    public static LocalImportGraph AllImports(IReadOnlyList<TsSourceFile> files) =>
        Build(files, _ => true);

    /// <summary>The imports of <paramref name="files"/> an ES module evaluates.</summary>
    public static LocalImportGraph ValueImports(IReadOnlyList<TsSourceFile> files) =>
        Build(files, import => !import.TypeOnly);

    /// <summary>The key of <paramref name="file"/>: its relative path without the <c>.ts</c> / <c>.tsx</c> extension.</summary>
    public static string KeyOf(TsSourceFile file) => ModulePath.Of(file.FileName);

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
            .Select(import => Resolve(import.From, _fileKeys))
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
        Func<TsImport, bool> isEdge
    )
    {
        var fileKeys = files.Select(KeyOf).ToHashSet(StringComparer.Ordinal);

        // Indexer writes, not ToDictionary: two files of one name (an MS0008
        // collision) keep the last, as the diagnostics that report it expect.
        var edges = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            edges[KeyOf(file)] = file
                .Statements.OfType<TsImport>()
                .Where(isEdge)
                .Select(import => Resolve(import.From, fileKeys))
                .OfType<string>()
                .ToList();
        }

        return new LocalImportGraph(edges, fileKeys);
    }

    /// <summary>The generated file <paramref name="module"/> names; <c>null</c> for an external package.</summary>
    private static string? Resolve(TsModuleSpecifier module, IReadOnlySet<string> fileKeys) =>
        module is TsLocalModule { FileKey: var key } && fileKeys.Contains(key) ? key : null;
}
