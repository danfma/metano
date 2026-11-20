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
