using System.Collections.Immutable;

namespace Conformance;

public class RoundOneItem { }

/// <summary>Independent review repros: reachability, typed empties, and widened factories.</summary>
public static class StageHRoundOneProbes
{
    public static string LocalStaticEmpty()
    {
        object value = ImmutableList<RoundOneItem>.Empty;
        return $"{value is ImmutableList<string>},{value is ImmutableList<RoundOneItem>}";
    }

    private static List<RoundOneItem> Read(KeyValuePair<int, List<RoundOneItem>> pair) =>
        pair.Value;

    public static string LocalStaticArrayEmpty()
    {
        object value = ImmutableArray<RoundOneItem>.Empty;
        return $"{value is ImmutableArray<string>},{value is ImmutableArray<RoundOneItem>}";
    }

    public static string LocalMemberRead()
    {
        object value = Read(new KeyValuePair<int, List<RoundOneItem>>(1, new List<RoundOneItem>()));
        return $"{value is List<string>},{value is List<RoundOneItem>}";
    }

    public static string ClearList()
    {
        var value = ImmutableList.Create(1).Clear();
        return $"{ReferenceEquals(ImmutableList<int>.Empty, value)},{(object)value is ImmutableList<int>},{(object)value is ImmutableList<string>}";
    }

    public static string ClearSet()
    {
        var value = ImmutableHashSet.Create(1).Clear();
        return $"{ReferenceEquals(ImmutableHashSet<int>.Empty, value)},{(object)value is ImmutableHashSet<int>},{(object)value is ImmutableHashSet<string>}";
    }

    public static string ClearDictionary()
    {
        var value = ImmutableDictionary.Create<int, int>().Add(1, 1).Clear();
        return $"{ReferenceEquals(ImmutableDictionary<int, int>.Empty, value)},{(object)value is ImmutableDictionary<int, int>},{(object)value is ImmutableDictionary<int, string>}";
    }

    public static string RemoveToEmpty()
    {
        var list = ImmutableList.Create(1);
        var set = ImmutableHashSet.Create(1);
        var map = ImmutableDictionary.Create<int, int>().Add(1, 1);
        return $"{ReferenceEquals(ImmutableList<int>.Empty, list.Remove(1))},"
            + $"{ReferenceEquals(ImmutableList<int>.Empty, list.RemoveAt(0))},"
            + $"{ReferenceEquals(ImmutableList<int>.Empty, list.RemoveRange(0, 1))},"
            + $"{ReferenceEquals(ImmutableHashSet<int>.Empty, set.Remove(1))},"
            + $"{ReferenceEquals(ImmutableHashSet<int>.Empty, set.Except(new[] { 1 }))},"
            + $"{ReferenceEquals(ImmutableDictionary<int, int>.Empty, map.Remove(1))},"
            + $"{ReferenceEquals(ImmutableDictionary<int, int>.Empty, map.RemoveRange(new[] { 1 }))}";
    }

    public static string WidenedList()
    {
        IEnumerable<object> source = ImmutableList.Create("x");
        object value = source.ToImmutableList();
        return $"{value is ImmutableList<object>},{value is ImmutableList<string>},{ReferenceEquals(source, value)}";
    }

    public static string WidenedArray()
    {
        IEnumerable<object> source = ImmutableList.Create("x");
        object value = source.ToImmutableArray();
        return $"{value is ImmutableArray<object>},{value is ImmutableArray<string>}";
    }

    public static string WidenedArrayReuse()
    {
        IEnumerable<object> source = ImmutableArray.Create("x");
        object value = source.ToImmutableArray();
        return $"{value is ImmutableArray<object>},{value is ImmutableArray<string>}";
    }

    public static string WidenedSet()
    {
        IEnumerable<object> source = ImmutableList.Create("x");
        object value = source.ToImmutableHashSet();
        return $"{value is ImmutableHashSet<object>},{value is ImmutableHashSet<string>}";
    }

    public static string WidenedSetReuse()
    {
        IEnumerable<object> source = ImmutableHashSet.Create("x");
        object value = source.ToImmutableHashSet();
        return $"{value is ImmutableHashSet<object>},{value is ImmutableHashSet<string>},{ReferenceEquals(source, value)}";
    }

    public static string SameSlotReuse()
    {
        var list = ImmutableList.Create("x");
        var set = ImmutableHashSet.Create("x");
        object listResult = list.ToImmutableList();
        object setResult = set.ToImmutableHashSet();
        return $"{ReferenceEquals(list, listResult)},{ReferenceEquals(set, setResult)},"
            + $"{listResult is ImmutableList<string>},{listResult is ImmutableList<object>},"
            + $"{setResult is ImmutableHashSet<string>},{setResult is ImmutableHashSet<object>}";
    }
}
