using System.Collections.Immutable;

namespace Conformance;

public static class StageHRoundThreeProbes
{
    private static ImmutableList<T> MakeList<T>(T value)
        where T : notnull => ImmutableList.Create(value);

    private static ImmutableArray<T> MakeArray<T>(T value)
        where T : notnull => ImmutableArray.Create(value);

    private static ImmutableHashSet<T> MakeSet<T>(T value)
        where T : notnull => ImmutableHashSet.Create(value);

    private static ImmutableDictionary<K, V> MakeDictionary<K, V>(K key, V value)
        where K : notnull
        where V : notnull => ImmutableDictionary.Create<K, V>().Add(key, value);

    public static string UnknownSourceList()
    {
        var source = MakeList("x");
        object result = source.ToImmutableList();
        return $"{result is ImmutableList<string>},{result is ImmutableList<int>}";
    }

    public static string UnknownSourceArray()
    {
        var source = MakeArray("x");
        object result = source.ToImmutableArray();
        return $"{result is ImmutableArray<string>},{result is ImmutableArray<int>}";
    }

    public static string UnknownSourceSet()
    {
        var source = MakeSet("x");
        object result = source.ToImmutableHashSet();
        return $"{result is ImmutableHashSet<string>},{result is ImmutableHashSet<int>}";
    }

    public static string UnknownSourceDictionary()
    {
        var source = MakeDictionary("k", "x");
        object result = source.ToImmutableDictionary();
        return $"{result is ImmutableDictionary<string, string>},{result is ImmutableDictionary<string, int>}";
    }

    private static bool SameReference(object left, object right) => ReferenceEquals(left, right);

    public static string SameTypeEmptyFactories() =>
        $"{SameReference(Lib.UnslottedImmutableFactories.IntList(), Lib.UnslottedImmutableFactories.IntList())},"
        + $"{SameReference(Lib.UnslottedImmutableFactories.IntSet(), Lib.UnslottedImmutableFactories.IntSet())},"
        + $"{SameReference(Lib.UnslottedImmutableFactories.IntDictionary(), Lib.UnslottedImmutableFactories.IntDictionary())}";
}
