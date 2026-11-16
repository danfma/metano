using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The import-graph pass of the reference casts (#669): a group whose file sits
/// on a cycle of value imports asserts every cast whose class it imports from a
/// module that reaches back to it, so the class import cannot close the cycle
/// (<c>Node.describe</c> → <c>Printer.print</c> → <c>castTo(n, Leaf)</c>, where
/// <c>Leaf</c> extends <c>Node</c>: loading <c>node</c> first evaluated
/// <c>class Leaf extends Node</c> before <c>Node</c> existed). The graph is the one
/// before any group is transformed again, so an import a reassertion drops never
/// unblocks another cast. A group reused from the cache carries no casts to read:
/// on a cycle it is transformed again first.
/// </summary>
internal sealed class ImportCycleCastPass(string? importAlias)
{
    /// <summary>The asserted classes of a group transformed with every cast checked.</summary>
    public static readonly IReadOnlySet<string> NoAssertedCasts = new HashSet<string>();

    /// <summary>
    /// Runs the pass over <paramref name="outcomes"/>; <paramref name="transformAgain"/>
    /// transforms the group at an index again with the classes whose casts it asserts,
    /// recording the result back into <paramref name="outcomes"/>.
    /// </summary>
    public void Run(GroupOutcomes outcomes, Action<int, IReadOnlySet<string>> transformAgain)
    {
        var graph = LocalImportGraph.ValueImports(
            [.. outcomes.Files.OfType<TsSourceFile>()],
            importAlias
        );

        for (var index = 0; index < outcomes.Files.Length; index++)
        {
            if (outcomes.Files[index] is not { } file || !IsOnImportCycle(graph, file))
                continue;

            if (outcomes.CheckedCasts[index] is null)
                transformAgain(index, NoAssertedCasts);

            var asserted = CastsClosingImportCycles(graph, outcomes, index);

            if (asserted.Count > 0)
                transformAgain(index, asserted);
        }
    }

    private static bool IsOnImportCycle(LocalImportGraph graph, TsSourceFile file)
    {
        var key = LocalImportGraph.KeyOf(file);

        return graph.TargetsOf(key).Any(target => graph.Reaches(target, key));
    }

    private static HashSet<string> CastsClosingImportCycles(
        LocalImportGraph graph,
        GroupOutcomes outcomes,
        int index
    )
    {
        var file = outcomes.Files[index]!;
        var key = LocalImportGraph.KeyOf(file);

        return outcomes.CheckedCasts[index]!
            .Where(name =>
                graph.ValueSourceOf(file, name) is { } source && graph.Reaches(source, key)
            )
            .ToHashSet(StringComparer.Ordinal);
    }
}
