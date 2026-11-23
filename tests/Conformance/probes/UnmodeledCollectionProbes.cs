using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Conformance;

/// <summary>A user collection whose own Add transforms each element.</summary>
public class UcTimesTen : IEnumerable<int>
{
    private readonly List<int> _items = new();

    public void Add(int x) => _items.Add(x * 10);

    public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Collection expressions over types the transpiler does not model (#581): an
/// order-preserving list-like keeps the array (MS0001 warning), a type that
/// sorts, deduplicates, reverses or runs a user Add throws (MS0001).
/// </summary>
public static class UnmodeledCollectionProbes
{
    public static string KeptCollection()
    {
        Collection<int> filled = [1, 2];
        Collection<int> empty = [];

        return string.Join(",", filled) + "|" + string.Join(",", empty) + "|";
    }

    public static string KeptObservableCollection()
    {
        ObservableCollection<int> items = [1, 2];

        return string.Join(",", items);
    }

    public static string KeptImmutableQueue()
    {
        ImmutableQueue<int> queue = [1, 2, 3];

        return string.Join(",", queue);
    }

    public static string KeptSingleElements()
    {
        ConcurrentBag<int> bag = [1];
        ImmutableSortedSet<int> sorted = [5];
        ImmutableStack<int> stack = [7];

        return string.Join(",", bag)
            + "|"
            + string.Join(",", sorted)
            + "|"
            + string.Join(",", stack);
    }

    public static string ThrowsSortedSet() => Attempt(() => string.Join(",", Sorted()));

    public static string ThrowsStack() => Attempt(() => string.Join(",", Stacked()));

    public static string ThrowsFrozenSet() => Attempt(() => Frozen().Count.ToString());

    public static string ThrowsBag() => Attempt(() => string.Join(",", Bagged().OrderBy(x => x)));

    public static string ThrowsUserAdd() => Attempt(() => string.Join(",", TimesTen()));

    private static ImmutableSortedSet<int> Sorted() => [3, 1, 2];

    private static ImmutableStack<int> Stacked() => [1, 2, 3];

    private static FrozenSet<int> Frozen() => [1, 2, 2];

    private static ConcurrentBag<int> Bagged() => [1, 2];

    private static UcTimesTen TimesTen() => [1, 2];

    private static string Attempt(Func<string> build)
    {
        try
        {
            return build();
        }
        catch (Exception)
        {
            return "throws";
        }
    }
}
