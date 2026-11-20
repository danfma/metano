using Metano.Compiler.Diagnostics;

namespace Metano.Tests;

/// <summary>
/// The read-only, immutable, sorted and concurrent collections as their
/// metano-runtime classes (spec 011 FR-054, contracts/runtime-collections.md §
/// C# member → emitted TS (011h)): constructors and factories, the of/from
/// rule, collection expressions and initializers, element access, the
/// <c>Try*</c> tuple lowering and the builders, one golden per family.
/// </summary>
public class StageHCollectionGoldenTests
{
    [Test]
    public async Task ReadOnlyCollection_MatchesTheGoldenOutput() =>
        await AssertGolden(
            "collections-read-only.ts",
            "views.ts",
            """
            using System;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            [Transpile]
            public static class Views
            {
                public static ReadOnlyCollection<int> Wrap(List<int> list) => list.AsReadOnly();

                public static ReadOnlyCollection<string> OfArray(string[] items) => Array.AsReadOnly(items);

                public static ReadOnlyCollection<int> Construct(IList<int> source) =>
                    new ReadOnlyCollection<int>(source);

                public static ReadOnlyCollection<int> None() => ReadOnlyCollection<int>.Empty;

                public static string Describe(ReadOnlyCollection<int> view)
                {
                    var first = view.Count > 0 ? view[0] : -1;
                    var last = view.Count > 0 ? view[^1] : -1;
                    var copy = new int[view.Count];
                    view.CopyTo(copy, 0);
                    return $"{view.Count}:{first}:{last}:{view.Contains(2)}:{view.IndexOf(3)}:{copy.Length}";
                }

                public static int Sum(ReadOnlyCollection<int> view)
                {
                    var sum = 0;
                    foreach (var item in view)
                        sum += item;
                    return sum;
                }

                public static IReadOnlyList<int> AsInterface(ReadOnlyCollection<int> view) => view;
            }
            """
        );

    [Test]
    public async Task ImmutableCollections_MatchTheGoldenOutput() =>
        await AssertGolden(
            "collections-immutable.ts",
            "snapshots.ts",
            """
            using System.Collections.Generic;
            using System.Collections.Immutable;

            [Transpile]
            public static class Snapshots
            {
                public static ImmutableList<int> Listed(int[] source) => ImmutableList.Create(source);

                public static ImmutableList<int> Fresh() => ImmutableList.Create(1, 2, 3);

                public static ImmutableList<string> Expression() => ["a", "b"];

                public static ImmutableList<int> None() => ImmutableList<int>.Empty;

                public static ImmutableList<int> Edited(ImmutableList<int> list) =>
                    list.Add(4)
                        .AddRange([5, 6])
                        .Insert(0, 0)
                        .Remove(2)
                        .RemoveAt(0)
                        .SetItem(0, 9)
                        .RemoveRange([5])
                        .RemoveRange(0, 1)
                        .Sort()
                        .Reverse();

                public static int Searches(ImmutableList<int> list) =>
                    list.IndexOf(3) + list.Find(x => x > 1) + list.FindIndex(x => x > 1)
                    + list.BinarySearch(3) + list[0] + list[^1] + list.Count;

                public static ImmutableList<int> Built(ImmutableList<int> list)
                {
                    var builder = list.ToBuilder();
                    builder.Add(7);
                    builder[0] = 1;
                    return builder.ToImmutable();
                }

                public static ImmutableArray<int> Array(int[] source) => ImmutableArray.Create(source);

                public static ImmutableArray<int> ArrayFresh() => ImmutableArray.Create(1, 2, 3, 4, 5);

                public static ImmutableArray<int> ArrayRange(int[] source) =>
                    ImmutableArray.Create(source, 1, 2);

                public static ImmutableArray<int> ArrayExpression() => [1, 2];

                public static ImmutableArray<int> ArrayEdited(ImmutableArray<int> array, int[] more) =>
                    array.Add(1).AddRange(more).AddRange(2, 3);

                public static string ArrayReads(ImmutableArray<int> array) =>
                    $"{array.Length}:{array[0]}:{array[^1]}:{string.Join(",", array[1..^1])}:{array.IsEmpty}";

                public static ImmutableArray<int> ArrayBuilt()
                {
                    var builder = ImmutableArray.CreateBuilder<int>();
                    builder.Add(1);
                    return builder.ToImmutable();
                }

                public static ImmutableHashSet<string> Set(IEqualityComparer<string> comparer) =>
                    ImmutableHashSet.Create(comparer, "a", "b");

                public static ImmutableHashSet<int> SetRange(IEnumerable<int> items) =>
                    items.ToImmutableHashSet();

                public static ImmutableHashSet<int> SetEdited(ImmutableHashSet<int> set) =>
                    set.Add(1).Remove(2).Union([3]).Except([4]);

                public static bool SetFound(ImmutableHashSet<int> set) =>
                    set.TryGetValue(1, out var actual) && actual == 1;

                public static ImmutableDictionary<string, int> Dictionary() =>
                    ImmutableDictionary<string, int>.Empty.Add("a", 1).SetItem("b", 2).Remove("c");

                public static ImmutableDictionary<string, int> DictionaryExpression(
                    Dictionary<string, int> source
                ) => [.. source, new("k", 0)];

                public static ImmutableDictionary<string, int> Selected(List<string> words) =>
                    words.ToImmutableDictionary(w => w, w => w.Length);

                public static int Lookup(ImmutableDictionary<string, int> map) =>
                    map.TryGetValue("a", out var value)
                        ? value + map.GetValueOrDefault("b") + map["a"]
                        : map.Count;

                public static ImmutableDictionary<string, int> DictionaryBuilt(
                    ImmutableDictionary<string, int> map
                )
                {
                    var builder = map.ToBuilder();
                    builder["z"] = 26;
                    return builder.ToImmutable();
                }

                public static IImmutableList<int> AsInterface(ImmutableList<int> list) => list.Add(1);
            }
            """
        );

    [Test]
    public async Task SortedCollections_MatchTheGoldenOutput() =>
        await AssertGolden(
            "collections-sorted.ts",
            "ordered.ts",
            """
            using System.Collections.Generic;

            [Transpile]
            public static class Ordered
            {
                public static SortedSet<int> Set() => new SortedSet<int> { 3, 1, 2 };

                public static SortedSet<string> SetWithComparer(IComparer<string> comparer) =>
                    new SortedSet<string>(comparer) { "b", "a" };

                public static SortedSet<int> SetCopy(IEnumerable<int> items) => new SortedSet<int>(items);

                public static SortedSet<int> SetExpression(int[] more) => [5, .. more];

                public static string SetReads(SortedSet<int> set)
                {
                    set.Add(4);
                    set.Remove(1);
                    var view = set.GetViewBetween(2, 4);
                    return $"{set.Min}:{set.Max}:{set.Count}:{view.Count}:{string.Join(",", set.Reverse())}:{set.RemoveWhere(x => x > 10)}";
                }

                public static string? Smallest(SortedSet<string> set) => set.Min;

                public static SortedDictionary<string, int> Dictionary() =>
                    new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 };

                public static SortedDictionary<string, int> DictionaryAdded() =>
                    new() { { "b", 2 }, { "a", 1 } };

                public static SortedDictionary<string, int> DictionaryCopy(
                    IDictionary<string, int> source,
                    IComparer<string> comparer
                ) => new SortedDictionary<string, int>(source, comparer);

                public static string DictionaryReads(SortedDictionary<string, int> map)
                {
                    map["c"] = 3;
                    map["a"] += 10;
                    var found = map.TryGetValue("b", out var b) ? b : -1;
                    return $"{map["a"]}:{map.Count}:{string.Join(",", map.Keys)}:{map.Values.Count}:{map.GetValueOrDefault("z")}:{found}";
                }

                public static SortedList<string, int> List(int capacity) =>
                    new SortedList<string, int>(capacity) { ["x"] = 1 };

                public static SortedList<string, int> ListExpression() => new() { ["y"] = 2 };

                public static string ListReads(SortedList<string, int> list) =>
                    $"{list.Keys[0]}:{list.Values[0]}:{list.GetKeyAtIndex(0)}:{list.IndexOfKey("x")}:{list.IndexOfValue(1)}";
            }
            """
        );

    [Test]
    public async Task ConcurrentDictionary_MatchesTheGoldenOutput() =>
        await AssertGolden(
            "collections-concurrent.ts",
            "caches.ts",
            """
            using System.Collections.Concurrent;
            using System.Collections.Generic;

            [Transpile]
            public static class Caches
            {
                public static ConcurrentDictionary<string, int> Build() =>
                    new ConcurrentDictionary<string, int> { ["a"] = 1 };

                public static ConcurrentDictionary<string, int> None() => [];

                public static ConcurrentDictionary<string, int> Copy(
                    IEnumerable<KeyValuePair<string, int>> source
                ) => new ConcurrentDictionary<string, int>(source);

                public static ConcurrentDictionary<string, int> Sized() =>
                    new ConcurrentDictionary<string, int>(4, 16);

                public static string Use(ConcurrentDictionary<string, int> cache)
                {
                    var a = cache.GetOrAdd("a", 1);
                    var b = cache.GetOrAdd("b", key => key.Length);
                    var c = cache.GetOrAdd("c", (key, extra) => key.Length + extra, 10);
                    var d = cache.AddOrUpdate("a", 0, (key, old) => old + 1);
                    var e = cache.AddOrUpdate("e", key => 5, (key, old) => old * 2);
                    cache["f"] = 6;
                    cache["f"] += 1;
                    var added = cache.TryAdd("g", 7);
                    var updated = cache.TryUpdate("g", 8, 7);
                    var removed = cache.TryRemove("a", out var gone);
                    var found = cache.TryGetValue("b", out var value);
                    return $"{a}{b}{c}{d}{e}{added}{updated}{removed}{gone}{found}{value}{cache.Count}{cache.IsEmpty}{cache.GetValueOrDefault("zz")}";
                }
            }
            """
        );

    private static async Task AssertGolden(string golden, string file, string source)
    {
        var (result, diagnostics) = TranspileHelper.TranspileWithDiagnostics(source);

        await Assert
            .That(diagnostics.Where(d => d.Severity != MetanoDiagnosticSeverity.Info))
            .IsEmpty();

        await TranspileHelper.AssertGolden(golden, result[file]);
    }
}
