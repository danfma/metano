using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Conformance;

/// <summary>H/J integration: closed arguments survive factories, builders and JSON-adjacent shapes.</summary>
public static class StageHCollectionSlotProbes
{
    public static string ImmutableFactories()
    {
        object list = ImmutableList.Create(1, 2);
        object array = ImmutableArray.Create(1, 2);
        object set = ImmutableHashSet.Create(1, 2);
        object map = new[] { 1 }.ToImmutableDictionary(x => x, x => x);

        return $"{list is ImmutableList<int>},{list is ImmutableList<string>},"
            + $"{array is ImmutableArray<int>},{array is ImmutableArray<string>},"
            + $"{set is ImmutableHashSet<int>},{set is ImmutableHashSet<string>},"
            + $"{map is ImmutableDictionary<int, int>},{map is ImmutableDictionary<int, string>}";
    }

    public static string EmptySpecializations()
    {
        object list = ImmutableList<int>.Empty;
        object array = ImmutableArray<int>.Empty;
        object set = ImmutableHashSet<int>.Empty;
        object map = ImmutableDictionary<int, int>.Empty;

        return $"{list is ImmutableList<int>},{list is ImmutableList<string>},"
            + $"{array is ImmutableArray<int>},{array is ImmutableArray<string>},"
            + $"{set is ImmutableHashSet<int>},{set is ImmutableHashSet<string>},"
            + $"{map is ImmutableDictionary<int, int>},{map is ImmutableDictionary<int, string>},"
            + $"{ReferenceEquals(list, ImmutableList<int>.Empty)}";
    }

    public static string BuilderSlots()
    {
        object list = ImmutableList.CreateBuilder<int>();
        object array = ImmutableArray.CreateBuilder<int>();
        object set = ImmutableHashSet.CreateBuilder<int>();
        object map = ImmutableDictionary.CreateBuilder<int, int>();

        return $"{list is ImmutableList<int>.Builder},{list is ImmutableList<string>.Builder},"
            + $"{array is ImmutableArray<int>.Builder},{array is ImmutableArray<string>.Builder},"
            + $"{set is ImmutableHashSet<int>.Builder},{set is ImmutableHashSet<string>.Builder},"
            + $"{map is ImmutableDictionary<int, int>.Builder},{map is ImmutableDictionary<int, string>.Builder}";
    }

    public static string BuilderConversions()
    {
        object list = ImmutableList.CreateBuilder<int>().ToImmutable();
        object array = ImmutableArray.CreateBuilder<int>().ToImmutable();
        object set = ImmutableHashSet.CreateBuilder<int>().ToImmutable();
        object map = ImmutableDictionary.CreateBuilder<int, int>().ToImmutable();

        return $"{list is ImmutableList<int>},{list is ImmutableList<string>},"
            + $"{array is ImmutableArray<int>},{array is ImmutableArray<string>},"
            + $"{set is ImmutableHashSet<int>},{set is ImmutableHashSet<string>},"
            + $"{map is ImmutableDictionary<int, int>},{map is ImmutableDictionary<int, string>}";
    }

    public static string PersistentResultsAndBuilders()
    {
        object list = ImmutableList.Create(1).Add(2).ToBuilder();
        object array = ImmutableArray.Create(1).Add(2).ToBuilder();
        object set = ImmutableHashSet.Create(1).Add(2).ToBuilder();
        object map = ImmutableDictionary.Create<int, int>().Add(1, 2).ToBuilder();

        return $"{list is ImmutableList<int>.Builder},{list is ImmutableList<string>.Builder},"
            + $"{array is ImmutableArray<int>.Builder},{array is ImmutableArray<string>.Builder},"
            + $"{set is ImmutableHashSet<int>.Builder},{set is ImmutableHashSet<string>.Builder},"
            + $"{map is ImmutableDictionary<int, int>.Builder},{map is ImmutableDictionary<int, string>.Builder}";
    }

    public static string ImmutableInterfaces()
    {
        object list = ImmutableList.Create(1);
        object array = ImmutableArray.Create(1);
        object set = ImmutableHashSet.Create(1);
        object map = ImmutableDictionary.Create<int, int>();

        return $"{list is IImmutableList<int>},{list is IImmutableList<string>},"
            + $"{array is IImmutableList<int>},{array is IImmutableList<string>},"
            + $"{set is IImmutableSet<int>},{set is IImmutableSet<string>},"
            + $"{map is IImmutableDictionary<int, int>},{map is IImmutableDictionary<int, string>}";
    }

    public static string SortedAndConcurrent()
    {
        object set = new SortedSet<int> { 2, 1 };
        object map = new SortedDictionary<int, int> { [1] = 2 };
        object list = new SortedList<int, int>(3) { [1] = 2 };
        object concurrent = new ConcurrentDictionary<int, int>(1, 3);

        return $"{set is SortedSet<int>},{set is SortedSet<string>},"
            + $"{map is SortedDictionary<int, int>},{map is SortedDictionary<int, string>},"
            + $"{list is SortedList<int, int>},{list is SortedList<int, string>},"
            + $"{concurrent is ConcurrentDictionary<int, int>},{concurrent is ConcurrentDictionary<int, string>}";
    }

    public static string ReadOnlyFactories()
    {
        object direct = new ReadOnlyCollection<int>(new List<int> { 1 });
        object array = Array.AsReadOnly(new[] { 1 });
        object list = new List<int> { 1 }.AsReadOnly();

        return $"{direct is ReadOnlyCollection<int>},{direct is ReadOnlyCollection<string>},"
            + $"{array is ReadOnlyCollection<int>},{array is ReadOnlyCollection<string>},"
            + $"{list is ReadOnlyCollection<int>},{list is ReadOnlyCollection<string>}";
    }
}
