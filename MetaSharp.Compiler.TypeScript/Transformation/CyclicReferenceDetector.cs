using MetaSharp.Compiler.Diagnostics;
using MetaSharp.TypeScript.AST;

namespace MetaSharp.Transformation;

/// <summary>
/// Detects cyclic <c>#/</c> imports between the generated <see cref="TsSourceFile"/>s
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
/// Only intra-project paths (those starting with <c>#/</c>, the project's subpath import
/// alias) participate in the graph. Imports from external packages
/// (<c>@meta-sharp/runtime</c>, <c>@js-temporal/polyfill</c>, etc.) are skipped — their
/// resolution lives outside MetaSharp's view of the world.
/// </summary>
public static class CyclicReferenceDetector
{
    public static void DetectAndReport(
        IReadOnlyList<TsSourceFile> files,
        Action<MetaSharpDiagnostic> reportDiagnostic)
    {
        if (files.Count == 0) return;

        // Build the file index keyed by the kebab-cased relative path WITHOUT the .ts
        // extension, matching the form that #/-prefixed imports use.
        var byImportKey = new Dictionary<string, TsSourceFile>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var key = StripTsExtension(file.FileName);
            byImportKey[key] = file;
        }

        // Build the import graph. Edges point from a file to every file it imports via #/.
        var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var key = StripTsExtension(file.FileName);
            var targets = new List<string>();
            foreach (var stmt in file.Statements)
            {
                if (stmt is not TsImport import) continue;
                if (!import.From.StartsWith("#/", StringComparison.Ordinal)) continue;

                var targetKey = import.From[2..]; // strip "#/"
                if (byImportKey.ContainsKey(targetKey))
                    targets.Add(targetKey);
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
            if (visited.Contains(start)) continue;
            DfsVisit(start, graph, visited, onStack, pathStack, seenCycles, reportDiagnostic);
        }
    }

    private static void DfsVisit(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> onStack,
        List<string> pathStack,
        HashSet<string> seenCycles,
        Action<MetaSharpDiagnostic> reportDiagnostic)
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
                if (startIndex < 0) continue; // shouldn't happen, defensive
                var cycle = pathStack.GetRange(startIndex, pathStack.Count - startIndex);
                cycle.Add(target); // close the loop visually

                // Deduplicate equivalent cycles by hashing the canonical (rotated to
                // lex-min start) form. Without this, the same cycle could be reported
                // from each starting node we DFS into it from.
                var canonical = Canonicalize(cycle);
                if (seenCycles.Add(canonical))
                    reportDiagnostic(BuildDiagnostic(cycle));
            }
            else if (!visited.Contains(target))
            {
                DfsVisit(target, graph, visited, onStack, pathStack, seenCycles, reportDiagnostic);
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
        if (nodes.Count == 0) return "";

        var minIndex = 0;
        for (var i = 1; i < nodes.Count; i++)
        {
            if (string.CompareOrdinal(nodes[i], nodes[minIndex]) < 0)
                minIndex = i;
        }

        var rotated = nodes.Skip(minIndex).Concat(nodes.Take(minIndex));
        return string.Join("→", rotated);
    }

    private static MetaSharpDiagnostic BuildDiagnostic(IReadOnlyList<string> cycle)
    {
        var chain = string.Join(" → ", cycle.Select(n => $"#/{n}"));
        return new MetaSharpDiagnostic(
            MetaSharpDiagnosticSeverity.Warning,
            DiagnosticCodes.CyclicReference,
            $"Cyclic import detected: {chain}. The TypeScript compiler may emit confusing errors; consider extracting the shared piece into its own file.");
    }

    private static string StripTsExtension(string fileName) =>
        fileName.EndsWith(".ts", StringComparison.Ordinal)
            ? fileName[..^3]
            : fileName;
}
