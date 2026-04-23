using Metano.TypeScript.AST;

namespace Metano.Transformation;

/// <summary>
/// Generates leaf-only `index.ts` barrel files for the TypeScript output.
///
/// One barrel is emitted per directory that contains generated type files; sub-directories
/// are NOT re-exported (consumers must use full subpath imports such as
/// `import { Issue } from "package/issues/domain"`). When a directory already contains a
/// user-defined type whose file is named `index.ts`, the barrel is skipped to avoid a
/// collision.
///
/// Pure / stateless: takes the list of generated <see cref="TsSourceFile"/>s and returns
/// the index files to append to it.
/// </summary>
public static class BarrelFileGenerator
{
    /// <summary>
    /// Generates the barrel files. When <paramref name="namespaceBarrels"/> is
    /// <c>true</c>, additionally aggregates every leaf barrel into a
    /// root <c>src/index.ts</c> via nested <c>export namespace</c>
    /// blocks that mirror the C# namespace hierarchy. Consumers can
    /// then import from the package root (<c>import { Issues } from "@scope/pkg"</c>)
    /// and walk into nested namespaces. Tree-shaking stays intact
    /// because the root barrel binds each subpath to a single
    /// namespace import — no <c>export *</c> aggregation.
    /// <para>
    /// When the project already has types at the bare root namespace,
    /// the namespace-aggregation statements are appended to the
    /// existing root leaf so both the bare-root re-exports and the
    /// nested namespace bindings coexist in a single
    /// <c>index.ts</c>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TsSourceFile> Generate(
        IReadOnlyList<TsSourceFile> typeFiles,
        bool namespaceBarrels = false
    )
    {
        var leafBarrels = GenerateLeafBarrels(typeFiles);
        if (!namespaceBarrels)
            return leafBarrels;

        var aggregationStatements = BuildNamespaceAggregationStatements(leafBarrels);
        if (aggregationStatements.Count == 0)
            return leafBarrels;

        // Merge with an existing root leaf when present so bare-root
        // re-exports and the namespace-aggregation block share a
        // single index.ts.
        var merged = new List<TsSourceFile>(leafBarrels.Count + 1);
        var foundRoot = false;
        foreach (var barrel in leafBarrels)
        {
            if (
                !foundRoot && barrel.FileName.Equals("index.ts", StringComparison.OrdinalIgnoreCase)
            )
            {
                foundRoot = true;
                var combined = new List<TsTopLevel>(
                    barrel.Statements.Count + aggregationStatements.Count
                );
                combined.AddRange(barrel.Statements);
                combined.AddRange(aggregationStatements);
                merged.Add(barrel with { Statements = combined });
            }
            else
            {
                merged.Add(barrel);
            }
        }
        if (!foundRoot)
            merged.Add(new TsSourceFile("index.ts", aggregationStatements));
        return merged;
    }

    private static IReadOnlyList<TsSourceFile> GenerateLeafBarrels(
        IReadOnlyList<TsSourceFile> typeFiles
    )
    {
        // Group files by their directory
        var dirToFiles = new Dictionary<string, List<TsSourceFile>>();

        foreach (var file in typeFiles)
        {
            var dir = Path.GetDirectoryName(file.FileName)?.Replace('\\', '/') ?? "";
            if (!dirToFiles.TryGetValue(dir, out var list))
            {
                list = [];
                dirToFiles[dir] = list;
            }

            list.Add(file);
        }

        var indexFiles = new List<TsSourceFile>();

        foreach (var (dir, files) in dirToFiles)
        {
            var exports = new List<TsTopLevel>();

            foreach (var file in files.OrderBy(f => f.FileName))
            {
                var moduleName = Path.GetFileNameWithoutExtension(file.FileName);

                // Collect all exported names from this file. If a name has BOTH a value and a
                // type form (e.g., StringEnum: const + type alias, InlineWrapper: namespace + type),
                // re-export as value (declaration merging on the import side).
                var valueNames = new HashSet<string>(StringComparer.Ordinal);
                var typeOnlyNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (var stmt in file.Statements)
                {
                    var name = GetExportedName(stmt);
                    if (name is null)
                        continue;

                    if (IsTypeOnlyExport(stmt))
                        typeOnlyNames.Add(name);
                    else
                        valueNames.Add(name);
                }

                // A name that's both a value and a type → only emit as value (the import
                // pulls both via TS declaration merging).
                typeOnlyNames.ExceptWith(valueNames);

                if (valueNames.Count > 0)
                    exports.Add(new TsReExport([.. valueNames.OrderBy(n => n)], $"./{moduleName}"));

                if (typeOnlyNames.Count > 0)
                    exports.Add(
                        new TsReExport(
                            [.. typeOnlyNames.OrderBy(n => n)],
                            $"./{moduleName}",
                            TypeOnly: true
                        )
                    );
            }

            // Leaf-only barrels: do NOT re-export subdirectories. Consumers must use full
            // paths (e.g., `import { Issue } from "package/issues/domain/issue"`) or import
            // from the leaf barrel (`from "package/issues/domain"`).

            // Skip barrel generation if a user-defined type would collide with the barrel
            // file name (e.g., a type named "Index" produces "index.ts" already).
            var hasIndexCollision = files.Any(f =>
                Path.GetFileName(f.FileName).Equals("index.ts", StringComparison.OrdinalIgnoreCase)
            );
            if (hasIndexCollision)
                continue;

            if (exports.Count > 0)
            {
                var indexPath = dir.Length > 0 ? $"{dir}/index.ts" : "index.ts";
                indexFiles.Add(new TsSourceFile(indexPath, exports, ""));
            }
        }

        return indexFiles;
    }

    private static string? GetExportedName(TsTopLevel node) =>
        node switch
        {
            TsClass { Exported: true } c => c.Name,
            TsFunction { Exported: true } f => f.Name,
            TsEnum { Exported: true } e => e.Name,
            TsTypeAlias { Exported: true } t => t.Name,
            TsInterface { Exported: true } i => i.Name,
            TsConstObject { Exported: true } co => co.Name,
            TsNamespaceDeclaration { Exported: true } ns => ns.Name,
            _ => null,
        };

    /// <summary>
    /// Returns true if the export is type-only (no runtime value).
    /// Type aliases and interfaces are type-only. Classes, functions, enums, const objects,
    /// and namespaces are values.
    /// </summary>
    private static bool IsTypeOnlyExport(TsTopLevel node) => node is TsTypeAlias or TsInterface;

    /// <summary>
    /// Builds the namespace-aggregation statements (imports +
    /// <c>export namespace</c> blocks) that walk the leaf-barrel
    /// directory tree. Returns an empty list when there are no
    /// subdirectories to aggregate — bare-root-only projects stay
    /// leaf-only.
    /// </summary>
    private static IReadOnlyList<TsTopLevel> BuildNamespaceAggregationStatements(
        IReadOnlyList<TsSourceFile> leafBarrels
    )
    {
        // Collect the directory of every non-root leaf barrel — those
        // are the subpaths the root should aggregate.
        var leafDirs = leafBarrels
            .Select(b => Path.GetDirectoryName(b.FileName)?.Replace('\\', '/') ?? "")
            .Where(d => d.Length > 0)
            .Distinct()
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        if (leafDirs.Count == 0)
            return [];

        // Each leaf dir gets a single namespace import. Alias joins
        // PascalCased path segments with "_" and prefixes a `$` so the
        // binding can never collide with an exported namespace name —
        // a single-segment leaf (`shared-kernel`) would otherwise
        // produce `SharedKernel` both as the import alias AND as the
        // re-exported identifier, causing a TDZ
        // `Cannot access 'SharedKernel' before initialization`.
        var dirAliases = leafDirs.ToDictionary(
            d => d,
            d => "$" + string.Join("_", d.Split('/').Select(PathSegmentToPascal))
        );

        var statements = new List<TsTopLevel>();

        // Emit the imports first so the nested namespace block can
        // reference each alias by name.
        foreach (var dir in leafDirs)
        {
            statements.Add(
                new TsImport(Names: [dirAliases[dir]], From: $"./{dir}", IsNamespace: true)
            );
        }

        // Build a prefix tree of the path segments so the root emits
        // one `export namespace` per top-level segment with nested
        // re-exports inside.
        var root = new NamespaceNode();
        foreach (var dir in leafDirs)
        {
            var segments = dir.Split('/');
            var cursor = root;
            for (var i = 0; i < segments.Length; i++)
            {
                var pascal = PathSegmentToPascal(segments[i]);
                if (!cursor.Children.TryGetValue(pascal, out var next))
                {
                    next = new NamespaceNode();
                    cursor.Children[pascal] = next;
                }
                cursor = next;
            }
            cursor.LeafAlias = dirAliases[dir];
        }

        foreach (var (name, child) in root.Children.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            statements.Add(BuildNamespaceBlock(name, child));

        return statements;
    }

    private static TsTopLevel BuildNamespaceBlock(string name, NamespaceNode node)
    {
        var members = new List<TsTopLevel>();

        // A leaf of depth-1 (e.g., `SharedKernel` with no children)
        // collapses to a top-level `export import Alias = Alias;`
        // instead of an empty `namespace { }`. Still exposes the
        // binding under the package root.
        if (node.LeafAlias is not null && node.Children.Count == 0)
            return new TsExportImportAlias(name, node.LeafAlias);

        if (node.LeafAlias is not null)
        {
            // Rare shape: the same path is both a leaf (holds its own
            // barrel) and a parent of further children. Expose the leaf
            // binding under a reserved `_` identifier so callers can
            // still reach it while the children continue as nested
            // namespaces. The reserved sentinel keeps the ambiguity
            // explicit instead of silently dropping one side.
            members.Add(new TsExportImportAlias("_", node.LeafAlias));
        }

        foreach (
            var (childName, childNode) in node.Children.OrderBy(
                kv => kv.Key,
                StringComparer.Ordinal
            )
        )
            members.Add(BuildNamespaceBlock(childName, childNode));

        return new TsNamespaceDeclaration(
            Name: name,
            Functions: [],
            Exported: true,
            Members: members
        );
    }

    /// <summary>
    /// Converts a kebab-case (or already-PascalCased) directory segment
    /// into a PascalCase TS identifier — e.g. <c>shared-kernel</c> →
    /// <c>SharedKernel</c>. Mirrors the segment casing consumers would
    /// see walking the namespace tree on the C# side.
    /// </summary>
    private static string PathSegmentToPascal(string segment)
    {
        if (segment.Length == 0)
            return segment;
        var parts = segment.Split('-');
        var result = new System.Text.StringBuilder(segment.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0)
                continue;
            result.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                result.Append(part, 1, part.Length - 1);
        }
        return result.ToString();
    }

    private sealed class NamespaceNode
    {
        public Dictionary<string, NamespaceNode> Children { get; } = new(StringComparer.Ordinal);
        public string? LeafAlias { get; set; }
    }
}
