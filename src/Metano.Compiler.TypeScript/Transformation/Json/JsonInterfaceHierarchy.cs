using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The order in which System.Text.Json collects the properties of an
/// interface contract (FR-018): the interface itself, then every interface it
/// extends, sorted topologically with Kahn's algorithm over each node's
/// <c>AllInterfaces</c>, exactly as the source generator's
/// <c>TraverseGraphWithTopologicalSort</c> does. Measured on .NET 10:
/// <c>ID : IB, IC, IE</c> with <c>IB : IA</c> and <c>IE : IA</c> writes
/// <c>{"D":4,"E":5,"B":2,"A":1,"C":3}</c>, and <c>IP0 : IQ0, IRr0</c> with
/// <c>IQ0 : IS0</c> and <c>IRr0 : IT0, IS0</c> writes
/// <c>{"P0":5,"Rr0":4,"Q0":3,"S0":1,"T0":2}</c>.
/// </summary>
internal static class JsonInterfaceHierarchy
{
    public static List<INamedTypeSymbol> Of(INamedTypeSymbol entry)
    {
        var (nodes, pendingBasesOf) = BuildBaseInterfaceGraph(entry);

        return SortTopologically(nodes, pendingBasesOf);
    }

    /// <summary>
    /// Every interface reachable from <paramref name="entry"/>, numbered in
    /// discovery order (the entry is 0), and for each the indices of its
    /// <c>AllInterfaces</c>.
    /// </summary>
    private static (
        List<INamedTypeSymbol> Nodes,
        List<HashSet<int>> PendingBasesOf
    ) BuildBaseInterfaceGraph(INamedTypeSymbol entry)
    {
        var nodes = new List<INamedTypeSymbol> { entry };

        var indexOf = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default)
        {
            [entry] = 0,
        };

        var pendingBasesOf = new List<HashSet<int>>();

        for (var position = 0; position < nodes.Count; position++)
        {
            var bases = new HashSet<int>();

            foreach (var baseInterface in nodes[position].AllInterfaces)
            {
                if (!indexOf.TryGetValue(baseInterface, out var baseIndex))
                {
                    baseIndex = nodes.Count;
                    indexOf.Add(baseInterface, baseIndex);
                    nodes.Add(baseInterface);
                }

                bases.Add(baseIndex);
            }

            pendingBasesOf.Add(bases);
        }

        return (nodes, pendingBasesOf);
    }

    /// <summary>
    /// Kahn's algorithm as System.Text.Json runs it: nodes without pending
    /// bases are placed from the end of the result, in queue order; placing
    /// one frees, in index order, every node whose last pending base it was.
    /// </summary>
    private static List<INamedTypeSymbol> SortTopologically(
        List<INamedTypeSymbol> nodes,
        List<HashSet<int>> pendingBasesOf
    )
    {
        var withoutPendingBases = new Queue<int>(
            Enumerable.Range(0, nodes.Count).Where(index => pendingBasesOf[index].Count == 0)
        );

        var sorted = new INamedTypeSymbol[nodes.Count];
        var next = sorted.Length;

        while (withoutPendingBases.TryDequeue(out var placed))
        {
            sorted[--next] = nodes[placed];

            for (var index = 0; index < pendingBasesOf.Count; index++)
            {
                var becameFree =
                    pendingBasesOf[index].Remove(placed) && pendingBasesOf[index].Count == 0;

                if (becameFree)
                    withoutPendingBases.Enqueue(index);
            }
        }

        return sorted.ToList();
    }
}
