using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// The order the types of one output file (<c>[EmitInFile]</c>) are printed in:
/// declaration order, except that a class whose base class shares the file
/// moves directly after it (spec 001 FR-066). A class's <c>extends</c> clause
/// reads its base when the module loads, so a derived class printed first
/// fails with a <c>ReferenceError</c>. A file without such a pair keeps its order.
/// </summary>
public static class CoLocatedTypeOrder
{
    public static IReadOnlyList<INamedTypeSymbol> BasesFirst(IReadOnlyList<INamedTypeSymbol> types)
    {
        var inFile = types.ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var placed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var ordered = new List<INamedTypeSymbol>(types.Count);

        var waitingOnBase = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(
            SymbolEqualityComparer.Default
        );

        foreach (var type in types)
        {
            if (BaseInFile(type, inFile) is { } baseType && !placed.Contains(baseType))
            {
                WaitersOf(waitingOnBase, baseType).Add(type);
                continue;
            }

            Place(type, ordered, placed, waitingOnBase);
        }

        return ordered;
    }

    private static INamedTypeSymbol? BaseInFile(
        INamedTypeSymbol type,
        IReadOnlySet<INamedTypeSymbol> inFile
    ) =>
        type.BaseType?.OriginalDefinition is { } baseType && inFile.Contains(baseType)
            ? baseType
            : null;

    private static void Place(
        INamedTypeSymbol type,
        List<INamedTypeSymbol> ordered,
        HashSet<INamedTypeSymbol> placed,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> waitingOnBase
    )
    {
        ordered.Add(type);
        placed.Add(type);

        if (!waitingOnBase.Remove(type, out var derived))
            return;

        foreach (var waiting in derived)
            Place(waiting, ordered, placed, waitingOnBase);
    }

    private static List<INamedTypeSymbol> WaitersOf(
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> waitingOnBase,
        INamedTypeSymbol baseType
    )
    {
        if (!waitingOnBase.TryGetValue(baseType, out var waiters))
        {
            waiters = [];
            waitingOnBase[baseType] = waiters;
        }

        return waiters;
    }
}
