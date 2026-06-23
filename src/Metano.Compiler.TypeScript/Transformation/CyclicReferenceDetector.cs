using Metano.Compiler.Diagnostics;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Detects cyclic local-package imports between the generated <see cref="TsSourceFile"/>s
/// and emits a <c>MS0005</c> warning per cycle. Without this check, a cyclic chain only
/// surfaces downstream when the consumer's TypeScript compiler trips over it with a
/// confusing error — by then the user has to read the import lines manually to figure
/// out who imports who.
///
/// Algorithm: build a directed graph (<c>file path</c> → <c>set of imported file paths</c>)
/// from each file's <see cref="TsImport"/> entries, then walk it via iterative DFS with
/// a "currently visiting" stack. A back-edge (target already on the stack) marks a cycle;
/// the cycle is reconstructed by slicing the stack from the target to the current node.
/// Each distinct cycle is reported once — the visited set guarantees we don't re-enter
/// nodes whose subgraphs have already been explored.
///
/// Only intra-project paths (the configurable root alias — <c>#</c> by default, or
/// <c>#&lt;alias&gt;</c> when <c>--import-alias</c> is set — plus its <c>/</c> subpaths)
/// participate in the graph. Imports from external packages
/// (<c>metano-runtime</c>, <c>@js-temporal/polyfill</c>, etc.) are skipped — their
/// resolution lives outside Metano's view of the world.
/// </summary>
public static class CyclicReferenceDetector
{
    public static void DetectAndReport(
        IReadOnlyList<TsSourceFile> files,
        Action<MetanoDiagnostic> reportDiagnostic,
        string? importAlias = null
    )
    {
        if (files.Count == 0)
            return;

        var aliasPrefix = PathNaming.NormalizeImportAliasPrefix(importAlias);

        // Build the file index keyed by the kebab-cased relative path WITHOUT the .ts
        // extension. For index files we also register the directory barrel alias:
        //
        // - `index.ts`                → `#`
        // - `issues/domain/index.ts`  → `#/issues/domain`
        var byImportKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var key = StripTsExtension(file.FileName);
            byImportKey[key] = key;

            if (Path.GetFileName(key).Equals("index", StringComparison.Ordinal))
            {
                var parent = Path.GetDirectoryName(key)?.Replace('\\', '/') ?? "";
                if (parent.Length == 0)
                    byImportKey[""] = key;
                else
                    byImportKey[parent] = key;
            }
        }

        // Build the import graph. Edges point from a file to every file it imports via
        // the local package aliases (`#` or `#/...`) or relative paths (`./...`).
        var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var key = StripTsExtension(file.FileName);
            var targets = new List<string>();
            foreach (var stmt in file.Statements)
            {
                if (stmt is not TsImport import)
                    continue;
                if (
                    !TryNormalizeLocalImport(import.From, key, aliasPrefix, out var targetImportKey)
                )
                    continue;
                if (byImportKey.TryGetValue(targetImportKey, out var canonicalTarget))
                    targets.Add(canonicalTarget);
            }
            graph[key] = targets;
        }

        // Iterative DFS with a stack of (node, child-iterator) frames. The "onStack" set
        // tracks the current DFS path; visited tracks fully-explored nodes.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var pathStack = new List<string>();
        var seenCycles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in graph.Keys)
        {
            if (visited.Contains(start))
                continue;
            DfsVisit(
                start,
                graph,
                visited,
                onStack,
                pathStack,
                seenCycles,
                reportDiagnostic,
                aliasPrefix
            );
        }
    }

    private static void DfsVisit(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> onStack,
        List<string> pathStack,
        HashSet<string> seenCycles,
        Action<MetanoDiagnostic> reportDiagnostic,
        string aliasPrefix
    )
    {
        onStack.Add(node);
        pathStack.Add(node);

        foreach (var target in graph[node])
        {
            if (onStack.Contains(target))
            {
                // Back-edge → reconstruct the cycle by slicing the path stack from the
                // target (where the cycle starts) to the current node, then report it.
                var startIndex = pathStack.IndexOf(target);
                if (startIndex < 0)
                    continue; // shouldn't happen, defensive
                var cycle = pathStack.GetRange(startIndex, pathStack.Count - startIndex);
                cycle.Add(target); // close the loop visually

                // Deduplicate equivalent cycles by hashing the canonical (rotated to
                // lex-min start) form. Without this, the same cycle could be reported
                // from each starting node we DFS into it from.
                var canonical = Canonicalize(cycle);
                if (seenCycles.Add(canonical))
                    reportDiagnostic(BuildDiagnostic(cycle, aliasPrefix));
            }
            else if (!visited.Contains(target))
            {
                DfsVisit(
                    target,
                    graph,
                    visited,
                    onStack,
                    pathStack,
                    seenCycles,
                    reportDiagnostic,
                    aliasPrefix
                );
            }
        }

        onStack.Remove(node);
        pathStack.RemoveAt(pathStack.Count - 1);
        visited.Add(node);
    }

    /// <summary>
    /// Canonicalizes a cycle by rotating it so the lexicographically smallest node is
    /// first, then joining as a single string. Two reports of the same cycle (one
    /// starting from A → B → A, another from B → A → B) collapse to the same canonical
    /// form and only one diagnostic is emitted.
    /// </summary>
    private static string Canonicalize(IReadOnlyList<string> cycle)
    {
        // The closing node at the end is a duplicate of the first; drop it for canonicalization.
        var nodes = cycle.Take(cycle.Count - 1).ToList();
        if (nodes.Count == 0)
            return "";

        var minIndex = 0;
        for (var i = 1; i < nodes.Count; i++)
        {
            if (string.CompareOrdinal(nodes[i], nodes[minIndex]) < 0)
                minIndex = i;
        }

        var rotated = nodes.Skip(minIndex).Concat(nodes.Take(minIndex));
        return string.Join("→", rotated);
    }

    private static MetanoDiagnostic BuildDiagnostic(IReadOnlyList<string> cycle, string aliasPrefix)
    {
        var chain = string.Join(" → ", cycle.Select(key => ToDisplayImportPath(key, aliasPrefix)));
        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Warning,
            DiagnosticCodes.CyclicReference,
            $"Cyclic import detected: {chain}. The TypeScript compiler may emit confusing errors; consider extracting the shared piece into its own file."
        );
    }

    private static bool TryNormalizeLocalImport(
        string from,
        string importerKey,
        string aliasPrefix,
        out string key
    )
    {
        if (from == aliasPrefix)
        {
            key = "";
            return true;
        }

        var aliasSubpathPrefix = aliasPrefix + "/";
        if (from.StartsWith(aliasSubpathPrefix, StringComparison.Ordinal))
        {
            key = from[aliasSubpathPrefix.Length..];
            return true;
        }

        // Relative imports (./foo) — resolve against the importer's directory.
        if (from.StartsWith("./", StringComparison.Ordinal))
        {
            var dir = Path.GetDirectoryName(importerKey)?.Replace('\\', '/') ?? "";
            var relative = from[2..];
            key = dir.Length > 0 ? dir + "/" + relative : relative;
            return true;
        }

        key = "";
        return false;
    }

    private static string ToDisplayImportPath(string key, string aliasPrefix)
    {
        if (key == "index")
            return aliasPrefix;
        if (key.EndsWith("/index", StringComparison.Ordinal))
            return aliasPrefix + "/" + key[..^"/index".Length];
        return aliasPrefix + "/" + key;
    }

    private static string StripTsExtension(string fileName) =>
        fileName.EndsWith(".ts", StringComparison.Ordinal) ? fileName[..^3] : fileName;
}
