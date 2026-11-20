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
        object copied = new DispatchedBag(new[] { 2 });

        return $"{empty is List<int>},{empty is List<string>},{copied is IList<int>},{copied is IList<string>}";
    }

    public static string ReadOnlyLiteralsCarrySlots()
    {
        IReadOnlyList<int> xs = [1, 2];

        return $"{xs is IReadOnlyList<int>},{xs is IReadOnlyList<string>},{xs is IReadOnlyList<object>}";
    }

    public static string ConvertedArraysCarrySlots()
    {
        int[] array = [1];
        IList<int> xs = array;

        return $"{xs is IList<int>},{xs is IList<string>},{xs is IReadOnlyList<object>}";
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

        return $"{strings is List<string>},{strings is IReadOnlyList<object>},{slots is List<Slot>},"
            + $"{nullableInts is List<int?>},{nullableInts is List<int>},{nullableInts is IReadOnlyList<object>}";
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

    public static string ListTests()
    {
        object o = new List<int> { 1 };

        return $"{o is List<int>},{o is List<string>},{o is IList<int>},{o is IList<string>},"
            + $"{o is IReadOnlyList<int>},{o is ICollection<int>},{o is IReadOnlyCollection<int>}";
    }

    public static string CovarianceNeedsAReferenceConversion()
    {
        object ints = new List<int> { 1 };
        object strings = new List<string> { "a" };

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

        return $"{bag is List<int>},{bag is List<string>},{bag is IList<int>}";
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
