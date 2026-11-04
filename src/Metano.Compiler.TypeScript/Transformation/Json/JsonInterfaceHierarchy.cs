using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The order in which System.Text.Json collects the properties of an
/// interface contract (FR-018): the interface itself, then the interfaces it
/// extends, sorted topologically with Kahn's algorithm over the direct
/// <c>Interfaces</c> of each node, exactly as the source generator's
/// <c>TraverseGraphWithTopologicalSort</c> does. Measured on .NET 10:
/// <c>ID : IB, IC, IE</c> with <c>IB : IA</c> and <c>IE : IA</c> writes
/// <c>{"D":4,"E":5,"B":2,"A":1,"C":3}</c>.
/// </summary>
internal static class JsonInterfaceHierarchy
{
    public static List<INamedTypeSymbol> Of(INamedTypeSymbol entry)
    {
        var nodes = new List<INamedTypeSymbol> { entry };

        var indexOf = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default)
        {
            [entry] = 0,
        };

        var children = new List<HashSet<int>>();
        var childless = new Queue<int>();

        for (var position = 0; position < nodes.Count; position++)
        {
            var edges = new HashSet<int>();

            foreach (var child in nodes[position].Interfaces)
            {
                if (!indexOf.TryGetValue(child, out var childIndex))
                {
                    childIndex = nodes.Count;
                    indexOf.Add(child, childIndex);
                    nodes.Add(child);
                }

                edges.Add(childIndex);
            }

            children.Add(edges);

            if (edges.Count == 0)
                childless.Enqueue(position);
        }

        var sorted = new INamedTypeSymbol[nodes.Count];
        var next = sorted.Length;

        while (childless.TryDequeue(out var visited))
        {
            sorted[--next] = nodes[visited];

            for (var position = 0; position < children.Count; position++)
            {
                if (children[position].Remove(visited) && children[position].Count == 0)
                    childless.Enqueue(position);
            }
        }

        return sorted.ToList();
    }
}
