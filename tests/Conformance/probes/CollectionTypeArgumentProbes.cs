namespace Conformance;

/// <summary>
/// Spec 011 stage 011c, group J (FR-008, D31): collection runtime tests through type
/// arguments: classes and interfaces, covariance needing a reference conversion, concrete
/// arguments never matching another type, LINQ terminals and subclasses carrying the slot.
/// </summary>
public static class CollectionTypeArgumentProbes
{
    public record Slot(int Id);

    public class IntBag : List<int>;

    public class DispatchedBag : List<int>
    {
        public DispatchedBag()
            : this(new[] { 1 }) { }

        public DispatchedBag(IEnumerable<int> items)
            : base(items) { }
    }

    private static Func<int, int> KeySelector() => x => x;

    private static Func<int, int> ValueSelector() => x => x * 10;

    public static string DispatcherAndThisChainCarrySlots()
    {
        object empty = new DispatchedBag();
        List<int> copied = new DispatchedBag(new[] { 2 });

        return $"{empty is List<int>},{empty is List<string>},{copied is IList<int>},{copied is IList<string>}";
    }

    public static string ReadOnlyLiteralCount()
    {
        IReadOnlyList<int> xs = [1, 2];
        var count = xs.Count;

        return $"{count}";
    }

    public static string ConvertedArrayIsFixedSize()
    {
        int[] array = [1];
        IList<int> xs = array;

        return $"{xs.Count},{xs.IsReadOnly}";
    }

    public static string BoundLinqTerminalCarriesSlots()
    {
        object map = new[] { 1 }.ToDictionary(
            elementSelector: ValueSelector(),
            keySelector: KeySelector()
        );

        return $"{map is Dictionary<int, int>},{map is Dictionary<int, string>}";
    }

    public static string NullableLinqResultCarriesSlots()
    {
        IEnumerable<int>? xs = new[] { 1 };
        object? list = xs?.ToList();

        return $"{list is List<int>},{list is List<string>}";
    }

    public static string ReferenceAnnotationsAreErased()
    {
        object strings = new List<string?> { null };
        object slots = new List<Slot?> { null };
        object nullableInts = new List<int?> { null };
        List<string?> typedStrings = new() { null };
        List<int?> typedInts = new() { null };

        return $"{strings is List<string>},{typedStrings is IReadOnlyList<object>},{slots is List<Slot>},"
            + $"{nullableInts is List<int?>},{nullableInts is List<int>},{typedInts is IReadOnlyList<object>}";
    }

    public static string ReachedSubclassRetainsClassIdentity()
    {
        object bag = new IntBag();

        return $"{bag is IntBag},{bag.GetType() == typeof(IntBag)},{bag is List<int>},{bag is List<string>}";
    }

    public static string RawArrayInterfaceTestIsLoud()
    {
        int[] array = [1];

        return $"{array is IList<int>}";
    }

    public static string ObjectArrayInterfaceTestIsLoud()
    {
        object array = new[] { 1, 2 };

        return $"{array is IList<int>},{array is IReadOnlyList<int>}";
    }

    public static string EnumerableArrayInterfaceTestIsLoud()
    {
        IEnumerable<int> array = new[] { 1, 2 };

        return $"{array is IList<int>},{array is IReadOnlyList<int>}";
    }

    public static string ArrayAsInterfaceIsLoud()
    {
        object array = new[] { 1, 2 };

        return $"{array as IReadOnlyCollection<int> is not null}";
    }

    public static string DictionaryViewInterfaceTestIsLoud()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };
        object keys = map.Keys;
        object values = map.Values;

        return $"{keys is IReadOnlyCollection<string>},{values is ICollection<int>}";
    }

    public static string DirectDictionaryViewInterfaceTestIsLoud()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };
        Dictionary<string, int>.KeyCollection keys = map.Keys;

        return $"{keys is IReadOnlyCollection<string>},{keys is ICollection<string>},{map.Values is IReadOnlyCollection<int>}";
    }

    public static string ExplicitArrayCastKeepsTheSourceElement()
    {
        var items = new[] { new Slot(1) };
        object list = (IReadOnlyList<object>)items;

        return $"{list is List<string>}";
    }

    public static string ListClassTestsStayAnswerable()
    {
        object o = new List<int> { 1 };
        object d = new Dictionary<string, int>();

        return $"{o is List<int>},{o is List<string>},{d is Dictionary<string, int>},{d is IDictionary<string, int>}";
    }

    public static string ListTests()
    {
        object o = new List<int> { 1 };
        List<int> list = [1];

        return $"{o is List<int>},{o is List<string>},{list is IList<int>},{list is IList<string>},"
            + $"{list is IReadOnlyList<int>},{list is ICollection<int>},{list is IReadOnlyCollection<int>}";
    }

    public static string CovarianceNeedsAReferenceConversion()
    {
        List<int> ints = [1];
        List<string> strings = ["a"];

        return $"{ints is IReadOnlyList<object>},{strings is IReadOnlyList<object>},"
            + $"{ints is IReadOnlyCollection<int?>},{strings is IReadOnlyCollection<string>}";
    }

    public static string ConcreteArgumentsNeverMatchAnotherType()
    {
        object slots = new List<Slot> { new(1) };

        return slots switch
        {
            List<string> => "strings",
            List<Slot> => "slots",
            _ => "none",
        };
    }

    public static string DictionaryAndSetTests()
    {
        object d = new Dictionary<string, int> { ["a"] = 1 };
        object h = new HashSet<string> { "a" };

        return $"{d is Dictionary<string, int>},{d is Dictionary<string, string>},"
            + $"{d is IDictionary<string, int>},{d is IReadOnlyDictionary<string, int>},"
            + $"{h is HashSet<string>},{h is HashSet<int>},{h is ISet<string>}";
    }

    public static string LinqTerminalsCarryTheirArguments()
    {
        object list = new[] { 1, 2 }.ToList();
        object set = new[] { "a" }.ToHashSet();
        object map = new[] { "a" }.ToDictionary(s => s, s => s.Length);

        return $"{list is List<int>},{list is List<string>},{set is HashSet<string>},"
            + $"{set is HashSet<int>},{map is Dictionary<string, int>},{map is Dictionary<string, string>}";
    }

    public static string SubclassClosesItsArguments()
    {
        object bag = new IntBag { 1 };
        List<int> typed = new IntBag { 1 };

        return $"{bag is List<int>},{bag is List<string>},{typed is IList<int>}";
    }

    public static string TypePatternsOnTheSameValue()
    {
        object o = new List<int>();

        var first = o switch
        {
            List<string> => "s",
            List<int> => "i",
            _ => "?",
        };

        return first + (o as List<int> is not null) + (o as List<string> is null);
    }
}
