using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Collection lookups that compare a C# tuple by value. C# tuples are equal
/// element by element, so <c>list.Contains((1, "a"))</c>, a
/// <c>HashSet&lt;(int, int)&gt;</c> or a <c>Dictionary&lt;(int, int), V&gt;</c>
/// key finds an equal tuple. The tuple lowers to a JS array, which the lowered
/// lookups (<c>Array.includes</c> / <c>indexOf</c>, a JS <c>Map</c>, the runtime
/// <c>HashSet</c>) compare by reference, so the same code finds nothing. The
/// lowering stays, but each such use reports MS0001 so the divergence is
/// never silent.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string GenericCollectionsNamespace = "System.Collections.Generic";

    /// <summary>
    /// The members, per generic collection type, that compare their first
    /// type argument (the element, or the dictionary key) by equality; the
    /// constructor stands for the hashed collection itself.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> TupleSensitiveMembersByType = new(
        StringComparer.Ordinal
    )
    {
        ["List"] = ["Contains", "IndexOf", "LastIndexOf", "Remove"],
        ["IList"] = ["IndexOf"],
        ["ICollection"] = ["Contains", "Remove"],
        ["HashSet"] = [WellKnownMemberNames.InstanceConstructorName, "Add", "Contains", "Remove"],
        ["ISet"] = ["Add", "Contains", "Remove"],
        ["IReadOnlySet"] = ["Contains"],
        ["Dictionary"] =
        [
            WellKnownMemberNames.InstanceConstructorName,
            "Add",
            "ContainsKey",
            "Remove",
            "TryAdd",
            "TryGetValue",
        ],
        ["IDictionary"] = ["Add", "ContainsKey", "Remove", "TryGetValue"],
        ["IReadOnlyDictionary"] = ["ContainsKey", "TryGetValue"],
    };

    /// <summary>
    /// Reports MS0001 at <paramref name="node"/> when <paramref name="method"/>
    /// is a collection lookup over a tuple element or key
    /// (<see cref="TupleSensitiveMembersByType"/>).
    /// </summary>
    private static void ReportTupleValueLookup(SyntaxNode node, IMethodSymbol? method)
    {
        if (method is null || !ComparesTupleByValue(method))
            return;

        ReportUnsupportedFeature(
            node,
            $"Expression '{node}' compares C# tuples by value in a "
                + $"{method.ContainingType.Name}; the tuples lower to arrays, which the "
                + "emitted lookup compares by reference, so equal tuples are not found. "
                + "Use a record as the element or key instead."
        );
    }

    private static bool ComparesTupleByValue(IMethodSymbol method) =>
        method.ContainingType
            is { TypeArguments: [INamedTypeSymbol { IsTupleType: true }, ..] } type
        && type.ContainingNamespace?.ToDisplayString() == GenericCollectionsNamespace
        && TupleSensitiveMembersByType.TryGetValue(type.Name, out var members)
        && members.Contains(method.Name);
}
