using System.Collections.Immutable;

namespace Conformance;

public sealed class RoundTwoKey
{
    public override int GetHashCode() => 0;

    public override bool Equals(object? other) =>
        throw new InvalidOperationException("Unexpected comparison");
}

public static class StageHRoundTwoProbes
{
    public static string EmptyCollision()
    {
        var first = ImmutableDictionary.Create<RoundTwoKey, int>(null).Add(new RoundTwoKey(), 1);
        var second = ImmutableDictionary.Create<RoundTwoKey, int>(null).Add(new RoundTwoKey(), 2);
        return second.Count.ToString();
    }

    public static string EmptyBranches()
    {
        var map = ImmutableDictionary<int, int>.Empty;
        var list = ImmutableList<int>.Empty;
        var array = ImmutableArray<int>.Empty;
        var set = ImmutableHashSet<int>.Empty;
        var sum = 0;
        for (var i = 0; i < 100; i++)
        {
            var m = map.Add(i, i);
            var l = list.Add(i);
            var a = array.Add(i);
            var s = set.Add(i);
            sum += m[i] + l[0] + a[0] + (s.Contains(i) ? i : -1000);
        }
        return $"{sum},{map.Count},{list.Count},{array.Length},{set.Count}";
    }

    private static ImmutableList<T> ConvertList<T>(IEnumerable<T> source) =>
        source.ToImmutableList();

    private static ImmutableArray<T> ConvertArray<T>(IEnumerable<T> source) =>
        source.ToImmutableArray();

    private static ImmutableHashSet<T> ConvertSet<T>(IEnumerable<T> source) =>
        source.ToImmutableHashSet();

    private static ImmutableDictionary<K, V> ConvertDictionary<K, V>(
        IEnumerable<KeyValuePair<K, V>> source
    )
        where K : notnull => source.ToImmutableDictionary();

    public static string GenericList()
    {
        var source = ImmutableList.Create("x");
        object result = ConvertList(source);
        return $"{ReferenceEquals(source, result)},{result is ImmutableList<int>},{result is ImmutableList<string>}";
    }

    public static string GenericArray()
    {
        var source = ImmutableArray.Create("x");
        var converted = ConvertArray(source);
        object result = converted;
        return $"{source == converted},{result is ImmutableArray<int>},{result is ImmutableArray<string>}";
    }

    public static string GenericSet()
    {
        var source = ImmutableHashSet.Create("x");
        object result = ConvertSet(source);
        return $"{ReferenceEquals(source, result)},{result is ImmutableHashSet<int>},{result is ImmutableHashSet<string>}";
    }

    public static string GenericDictionary()
    {
        var source = ImmutableDictionary.Create<string, string>().Add("key", "x");
        object result = ConvertDictionary(source);
        return $"{ReferenceEquals(source, result)},{result is ImmutableDictionary<int, int>},{result is ImmutableDictionary<string, string>}";
    }

    public static string ClearedBuilders()
    {
        var set = ImmutableHashSet.Create(1).ToBuilder();
        var map = ImmutableDictionary.Create<int, int>().Add(1, 1).ToBuilder();
        set.Clear();
        map.Clear();
        object s = set.ToImmutable();
        object m = map.ToImmutable();
        return $"{ReferenceEquals(ImmutableHashSet<int>.Empty, s)},{ReferenceEquals(ImmutableDictionary<int, int>.Empty, m)},"
            + $"{s is ImmutableHashSet<int>},{s is ImmutableHashSet<string>},{m is ImmutableDictionary<int, int>},{m is ImmutableDictionary<string, int>}";
    }

    private static bool SameReference(object left, object right) => ReferenceEquals(left, right);

    public static string CrossTypeEmptyFactories()
    {
        return $"{SameReference(Lib.UnslottedImmutableFactories.IntList(), Lib.UnslottedImmutableFactories.StringList())},"
            + $"{SameReference(Lib.UnslottedImmutableFactories.IntSet(), Lib.UnslottedImmutableFactories.StringSet())},"
            + $"{SameReference(Lib.UnslottedImmutableFactories.IntDictionary(), Lib.UnslottedImmutableFactories.StringDictionary())}";
    }
}
