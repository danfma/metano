using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Conformance;

/// <summary>
/// Spec 011 stage 011h, compiler half: the read-only, immutable, sorted and
/// concurrent collections as metano-runtime classes, end to end (W4-11
/// condition 3 branching, the builder version rules F-a / F-b / F-c, the
/// ImmutableArray specifics F-d, the of/from rule, the mutable-interface
/// tests, the sorted and concurrent basics and the live read-only view).
/// Hash-ordered results print sorted: .NET's order is unspecified.
/// </summary>
public static class StageHCollectionProbes
{
    static string Join<T>(IEnumerable<T> items) => string.Join(",", items);

    static string Sorted(IEnumerable<int> items) => string.Join(",", items.OrderBy(x => x));

    public static string ImmutableListBranching()
    {
        var root = ImmutableList<int>.Empty.Add(1).Add(2);
        var left = root.Add(3);
        var right = root.Add(4);
        var removed = root.Remove(1);
        var set = root.SetItem(0, 9);
        var cleared = root.Clear();
        var again = left.Add(5);
        return $"{Join(root)}|{Join(left)}|{Join(right)}|{Join(removed)}|{Join(set)}|{cleared.Count}|{Join(again)}|{Join(left)}";
    }

    public static string ImmutableArrayBranching()
    {
        var root = ImmutableArray<int>.Empty.Add(1).Add(2);
        var left = root.Add(3);
        var right = root.Add(4);
        var removed = root.Remove(1);
        var set = root.SetItem(0, 9);
        return $"{Join(root)}|{Join(left)}|{Join(right)}|{Join(removed)}|{Join(set)}|{root.Clear().Length}";
    }

    public static string ImmutableHashSetBranching()
    {
        var root = ImmutableHashSet<int>.Empty.Add(1).Add(2);
        var left = root.Add(3);
        var right = root.Add(4);
        var removed = root.Remove(1);
        return $"{Sorted(root)}|{Sorted(left)}|{Sorted(right)}|{Sorted(removed)}|{root.Clear().Count}|{right.Contains(3)}";
    }

    public static string ImmutableDictionaryBranching()
    {
        var root = ImmutableDictionary<string, int>.Empty.Add("a", 1).Add("b", 2);
        var left = root.Add("c", 3);
        var right = root.Add("d", 4);
        var set = root.SetItem("a", 9);
        var removed = root.Remove("a");
        return $"{Pairs(root)}|{Pairs(left)}|{Pairs(right)}|{Pairs(set)}|{Pairs(removed)}|{root.Clear().Count}";
    }

    static string Pairs(IEnumerable<KeyValuePair<string, int>> map) =>
        string.Join(",", map.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

    public static string ImmutableEnumerationAfterBranch()
    {
        var root = ImmutableList.Create(1, 2, 3);
        var branch = root.RemoveAt(0).Add(4);
        var tip = root.Add(5);
        var seen = new List<int>();
        foreach (var x in root)
            seen.Add(x);
        return $"{Join(seen)}|{Join(branch)}|{Join(tip)}";
    }

    static string Modified(Action mutate, IEnumerable<int> source)
    {
        try
        {
            var first = true;
            foreach (var _ in source)
            {
                if (first)
                    mutate();

                first = false;
            }
            return "ok";
        }
        catch (InvalidOperationException)
        {
            return "threw";
        }
    }

    public static string ListBuilderVersion()
    {
        var b = ImmutableList.CreateBuilder<int>();
        b.Add(1);
        b.Add(2);
        var removeAbsent = Modified(() => b.Remove(9), b);
        var removeAllNone = Modified(() => b.RemoveAll(x => x > 9), b);
        var setSame = Modified(() => b[0] = b[0], b);
        return $"{removeAbsent},{removeAllNone},{setSame}";
    }

    public static string SetBuilderVersion()
    {
        var b = ImmutableHashSet.CreateBuilder<int>();
        b.Add(1);
        b.Add(2);
        var addPresent = Modified(() => b.Add(1), b);
        var removeAbsent = Modified(() => b.Remove(9), b);
        return $"{addPresent},{removeAbsent}";
    }

    public static string DictionaryBuilderVersion()
    {
        var b = ImmutableDictionary.CreateBuilder<int, int>();
        b.Add(1, 10);
        b.Add(2, 20);
        var removeAbsent = ModifiedPairs(() => b.Remove(9), b);
        var setSame = ModifiedPairs(() => b[1] = 10, b);
        var addEqual = ModifiedPairs(() => b.Add(1, 10), b);
        return $"{removeAbsent},{setSame},{addEqual}";
    }

    static string ModifiedPairs(Action mutate, IEnumerable<KeyValuePair<int, int>> source)
    {
        try
        {
            var first = true;
            foreach (var _ in source)
            {
                if (first)
                    mutate();

                first = false;
            }
            return "ok";
        }
        catch (InvalidOperationException)
        {
            return "threw";
        }
    }

    public static string ArrayBuilderNoVersionCheck()
    {
        var b = ImmutableArray.CreateBuilder<int>();
        b.Add(1);
        b.Add(2);
        var seen = new List<int>();
        var added = false;
        foreach (var x in b)
        {
            seen.Add(x);
            b[0] = 7;

            if (!added)
            {
                b.Add(3);
                added = true;
            }
        }
        return $"{Join(seen)}|{Join(b)}";
    }

    public static string DictionaryBuilderAdd()
    {
        var b = ImmutableDictionary.CreateBuilder<int, string>();
        b.Add(1, "a");
        b.Add(1, "a");
        var text = "none";

        try
        {
            b.Add(1, "b");
        }
        catch (ArgumentException ex)
        {
            text = ex.Message;
        }
        return $"{b.Count}|{text}";
    }

    public static string ImmutableArrayIndexer()
    {
        var a = ImmutableArray.Create(1, 2, 3);
        var text = "none";

        try
        {
            text = a[3].ToString();
        }
        catch (IndexOutOfRangeException ex)
        {
            text = ex.Message;
        }
        var negative = "none";

        try
        {
            negative = a[-1].ToString();
        }
        catch (IndexOutOfRangeException ex)
        {
            negative = ex.Message;
        }
        return $"{a[^1]}|{text}|{negative}";
    }

    public static string ImmutableArrayCopyTo()
    {
        var a = ImmutableArray.Create(1, 2, 3);
        var target = new int[5];
        a.CopyTo(target, 1);
        var shortText = "none";

        try
        {
            a.CopyTo(new int[2], 0);
        }
        catch (ArgumentException ex)
        {
            shortText = ex.Message;
        }
        return $"{Join(target)}|{shortText}";
    }

    public static string ImmutableArraySearchAndSort()
    {
        var a = ImmutableArray.Create(5, 1, 4);
        var sorted = a.Sort();
        var again = sorted.Sort();
        var index = sorted.IndexOf(4);
        var search = ImmutableArray.BinarySearch(sorted, 4);
        var reduced = sorted.BinarySearch(5);
        var missing = ImmutableArray.BinarySearch(sorted, 3);
        return $"{Join(sorted)}|{Join(again)}|{index}|{search}|{reduced}|{missing}|{a.IndexOf(9)}";
    }

    public static string ImmutableArrayLinqExtensions()
    {
        var items = ImmutableArray.Create(1, 2, 3);
        var projected = items.Where(x => x > 1).Select(x => x * 10);
        var first = items.First(x => x > 1);
        var last = ImmutableArrayExtensions.Last(items);
        var any = items.Any(x => x == 2);
        var none = ImmutableArray<int>.Empty.FirstOrDefault();
        return $"{Join(projected)}|{first},{last},{any},{none}";
    }

    public static string ImmutableArrayText() =>
        $"{ImmutableArray.Create(1, 2)}|{ImmutableList.Create(1)}";

    public static string CreateCopiesTheArray()
    {
        var source = new[] { 1, 2, 3 };
        var array = ImmutableArray.Create(source);
        var list = ImmutableList.Create(source);
        var set = ImmutableHashSet.Create(source);
        var range = ImmutableArray.Create(source, 1, 2);
        source[0] = 9;
        source[1] = 8;
        return $"{Join(array)}|{Join(list)}|{Sorted(set)}|{Join(range)}";
    }

    public static string CreateFromParams()
    {
        var array = ImmutableArray.Create(1, 2, 3, 4, 5, 6);
        var list = ImmutableList.Create(1, 2, 3);
        var single = ImmutableList.Create(7);
        var empty = ImmutableList.Create<int>();
        ImmutableArray<int> expression = [8, 9];
        return $"{Join(array)}|{Join(list)}|{Join(single)}|{empty.Count}|{Join(expression)}";
    }

    public static string FromRangeValidation()
    {
        var source = new[] { 1, 2, 3 };
        var text = "none";

        try
        {
            text = ImmutableArray.Create(source, 2, 5).Length.ToString();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            text = ex.ParamName ?? "";
        }
        return text;
    }

    public static string SortedSetBasics()
    {
        var set = new SortedSet<int> { 5, 1, 3, 1 };
        set.Add(4);
        set.Remove(5);
        var view = set.GetViewBetween(2, 4);
        var empty = new SortedSet<int>();
        SortedSet<string> names = ["b", "a"];
        var emptyNames = new SortedSet<string>();
        return $"{Join(set)}|{set.Min}|{set.Max}|{Join(view)}|{Join(set.Reverse())}|{empty.Min}|{empty.Max}|{Join(names)}|{emptyNames.Min ?? "null"}|{set.RemoveWhere(x => x > 3)}";
    }

    public static string SortedDictionaryBasics()
    {
        var map = new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 };
        map["c"] = 3;
        map["a"] += 10;
        var dup = "none";

        try
        {
            map.Add("b", 5);
        }
        catch (ArgumentException ex)
        {
            dup = ex.Message;
        }
        var found = map.TryGetValue("z", out var missing);
        return $"{string.Join(",", map.Select(p => $"{p.Key}={p.Value}"))}|{Join(map.Keys)}|{Join(map.Values)}|{found}:{missing}|{map.GetValueOrDefault("q")}|{dup}";
    }

    public static string SortedListBasics()
    {
        var list = new SortedList<string, int>(4) { ["y"] = 2 };
        list.Add("x", 1);
        list["z"] = 3;
        var removed = list.Remove("y");
        return $"{Join(list.Keys)}|{Join(list.Values)}|{list.Keys[0]}|{list.Values[1]}|{list.IndexOfKey("z")}|{list.GetKeyAtIndex(1)}|{removed}|{list.Count}";
    }

    public static string ConcurrentDictionaryBasics()
    {
        var cache = new ConcurrentDictionary<string, int> { ["a"] = 1 };
        var a = cache.GetOrAdd("a", 5);
        var b = cache.GetOrAdd("b", key => key.Length + 1);
        var c = cache.GetOrAdd("c", (key, extra) => extra, 30);
        var d = cache.AddOrUpdate("a", 0, (key, old) => old + 100);
        var e = cache.AddOrUpdate("e", key => 50, (key, old) => old * 2);
        cache["f"] = 6;
        cache["f"] += 1;
        var added = cache.TryAdd("a", 9);
        var updated = cache.TryUpdate("b", 22, 2);
        var removed = cache.TryRemove("c", out var gone);
        var notRemoved = cache.TryRemove("zz", out var none);
        var keys = string.Join(",", cache.Keys.OrderBy(k => k));
        return $"{a},{b},{c},{d},{e}|{added},{updated},{removed}:{gone},{notRemoved}:{none}|{keys}|{cache["b"]},{cache["f"]}|{cache.Count},{cache.IsEmpty}";
    }

    public static string ConcurrentDictionaryEnumerationNeverThrows()
    {
        var cache = new ConcurrentDictionary<int, int> { [1] = 1, [2] = 2 };
        var count = 0;
        foreach (var pair in cache)
        {
            count++;

            if (pair.Key == 1)
                cache.TryRemove(2, out _);
        }
        return $"{count}|{cache.Count}";
    }

    public static string AsReadOnlyIsLive()
    {
        var list = new List<int> { 1, 2 };
        var view = list.AsReadOnly();
        list.Add(3);
        var array = new[] { 4, 5 };
        var arrayView = Array.AsReadOnly(array);
        array[0] = 40;
        var wrapped = new ReadOnlyCollection<int>(list);
        list.Add(4);
        return $"{view.Count}:{view[2]}|{arrayView[0]}|{wrapped.Count}|{view.Contains(3)}:{view.IndexOf(2)}";
    }

    public static string MutableInterfaceTests()
    {
        object sorted = new SortedDictionary<int, int>();
        object concurrent = new ConcurrentDictionary<int, int>();
        object sortedSet = new SortedSet<int>();
        object immutableSet = ImmutableHashSet.Create(1);
        object immutableMap = ImmutableDictionary<int, int>.Empty;
        return $"{IsDictionary(sorted)},{IsDictionary(concurrent)},{IsSet(sortedSet)},{IsSet(immutableSet)},{IsDictionary(immutableMap)},{IsReadOnlySet(immutableSet)},{IsReadOnlySet(sortedSet)}";
    }

    private static bool IsDictionary(object value) => value is IDictionary<int, int>;

    private static bool IsSet(object value) => value is ISet<int>;

    private static bool IsReadOnlySet(object value) => value is IReadOnlySet<int>;
}
