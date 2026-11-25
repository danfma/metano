namespace Conformance;

/// <summary>
/// Wave 9 (#747): the <c>ILookup&lt;K, T&gt;</c> type, its indexer (an empty sequence for a
/// missing key), <c>Count</c> and <c>Contains</c>.
/// </summary>
public static class LookupProbes
{
    static ILookup<string, int> Build(IEnumerable<int> values) =>
        values.ToLookup(x => x % 2 == 0 ? "even" : "odd");

    static string Describe(ILookup<string, int> lookup, string key) =>
        $"{key}:{lookup[key].Count()}:{string.Join("|", lookup[key])}:{lookup.Contains(key)}";

    public static string IndexerPresentAndMissingKeys()
    {
        var lookup = Build([1, 2, 3, 4, 5]);
        return $"{lookup.Count},{Describe(lookup, "odd")},{Describe(lookup, "even")},{Describe(lookup, "none")}";
    }

    public static string EmptyLookup()
    {
        var lookup = Build([]);
        return $"{lookup.Count},{Describe(lookup, "odd")}";
    }

    public static string IndexerKeepsSourceOrderAndEnumeratesGroups()
    {
        var lookup = Build([3, 1, 4, 1, 5, 9, 2, 6]);
        var groups = string.Join(";", lookup.Select(g => g.Key + "=" + string.Join(",", g)));
        return $"{groups}/{string.Join(",", lookup["odd"].Select(x => x * 10))}";
    }
}
