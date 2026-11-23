using System.Collections.Immutable;

namespace Conformance;

public record CeItem(string Name, int Qty);

public record CeNested(CeItem Inner, string? Note);

public record struct CePoint(int X, int Y);

public enum CeColor
{
    Red = 1,
    Green = 2,
    Blue = 10,
}

public sealed class CeLengthComparer : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y) => x?.Length == y?.Length;

    public int GetHashCode(string value) => value.Length;
}

/// <summary>
/// Wave 6 group A: default sort order, value equality in collection lookups, equality
/// comparers and the immutable/sorted collection edge cases of #663, #664, #687, #710, #712.
/// </summary>
public static class CollectionEqualityProbes
{
    public static string SortLong()
    {
        var xs = new List<long> { 10, 9, 1, 100, -5 };
        xs.Sort();
        return string.Join(",", xs);
    }

    public static string SortEnum()
    {
        var xs = new List<CeColor> { CeColor.Blue, CeColor.Red, CeColor.Green };
        xs.Sort();
        return string.Join(",", xs);
    }

    public static string SortBoolAndNull()
    {
        var bs = new List<bool> { true, false, true };
        bs.Sort();
        var ns = new List<int?> { 3, null, 1 };
        ns.Sort();
        return string.Join(",", bs)
            + "|"
            + string.Join(",", ns.Select(n => n?.ToString() ?? "null"));
    }

    public static string SortDecimal()
    {
        var xs = new List<decimal> { 10m, 9m, 1m, 100m };
        xs.Sort();
        return string.Join(",", xs);
    }

    public static string SortGuid()
    {
        var xs = new List<Guid>
        {
            Guid.Parse("00000000-0000-0000-0000-0000000000ff"),
            Guid.Parse("00000000-0000-0000-0000-000000000009"),
        };
        xs.Sort();
        return string.Join(",", xs);
    }

    public static string SortArray()
    {
        var a = new[] { 10, 9, 1, 100 };
        Array.Sort(a);
        var b = new[] { 10, 9, 1, 100 };
        b.Sort();
        return string.Join(",", a) + "|" + string.Join(",", b);
    }

    public static string SortArrayLongAndDouble()
    {
        var a = new long[] { 10, 9, 1, 100 };
        Array.Sort(a);
        var d = new[] { 2.5, 10.0, 1.25 };
        Array.Sort(d);
        return string.Join(",", a) + "|" + string.Join(",", d);
    }

    public static string SortSpan()
    {
        var a = new[] { 10, 9, 1, 100 };
        Span<int> span = a;
        span.Sort();
        return string.Join(",", a);
    }

    public static string SortArrayWithComparison()
    {
        var a = new[] { 10, 9, 1, 100 };
        Array.Sort(a, (x, y) => y - x);
        return string.Join(",", a);
    }

    public static string SortKeysItems()
    {
        var keys = new[] { 3, 1, 2 };
        var items = new[] { "c", "a", "b" };
        Array.Sort(keys, items);
        return string.Join(",", keys) + "|" + string.Join(",", items);
    }

    public static string OrderByIdentity()
    {
        var xs = new List<int> { 10, 9, 1, 100 };
        return string.Join(",", xs.OrderBy(x => x));
    }

    public static string BinarySearchDefault()
    {
        var xs = new List<int> { 1, 9, 10, 100 };
        return xs.BinarySearch(10) + "," + xs.BinarySearch(11);
    }

    public static string RecordSearches()
    {
        var xs = new List<CeItem> { new("a", 1), new("b", 2), new("a", 1) };
        var probe = new CeItem("a", 1);
        return xs.Contains(probe)
            + ","
            + xs.IndexOf(probe)
            + ","
            + xs.LastIndexOf(probe)
            + ","
            + xs.Remove(new CeItem("b", 2))
            + ","
            + xs.Count;
    }

    public static string RecordStructSearches()
    {
        var xs = new List<CePoint> { new(1, 2), new(3, 4) };
        return xs.Contains(new CePoint(3, 4)) + "," + xs.IndexOf(new CePoint(3, 4));
    }

    public static string DateTimeSearches()
    {
        var xs = new List<DateTime> { new(2020, 1, 2), new(2021, 3, 4) };
        return xs.Contains(new DateTime(2021, 3, 4)) + "," + xs.IndexOf(new DateTime(2021, 3, 4));
    }

    public static string NaNSearches()
    {
        var xs = new List<double> { 1.0, double.NaN };
        return xs.Contains(double.NaN)
            + ","
            + xs.IndexOf(double.NaN)
            + ","
            + xs.LastIndexOf(double.NaN);
    }

    public static string ArraySearches()
    {
        var xs = new[] { new CeItem("a", 1), new CeItem("b", 2) };
        return Array.IndexOf(xs, new CeItem("b", 2))
            + ","
            + xs.Contains(new CeItem("a", 1))
            + ","
            + xs.IndexOf(new CeItem("b", 2))
            + ","
            + Array.LastIndexOf(xs, new CeItem("a", 1));
    }

    public static string ArrayNaNSearches()
    {
        var xs = new[] { 1.0, double.NaN };
        return Array.IndexOf(xs, double.NaN)
            + ","
            + xs.Contains(double.NaN)
            + ","
            + xs.IndexOf(double.NaN);
    }

    public static string StringSpanSearches()
    {
        var s = "hello";
        return s.Contains('e') + "," + s.IndexOf('l') + "," + s.LastIndexOf('l');
    }

    public static string RecordDictionaryKeys()
    {
        var d = new Dictionary<CeItem, string> { [new CeItem("a", 1)] = "x" };
        d[new CeItem("a", 1)] = "y";
        return d.Count
            + ","
            + d[new CeItem("a", 1)]
            + ","
            + d.ContainsKey(new CeItem("a", 1))
            + ","
            + d.Remove(new CeItem("a", 1))
            + ","
            + d.Count;
    }

    public static string RecordSetKeys()
    {
        var s = new HashSet<CeItem>();
        var first = s.Add(new CeItem("a", 1));
        var second = s.Add(new CeItem("a", 1));
        return first + "," + second + "," + s.Count + "," + s.Contains(new CeItem("a", 1));
    }

    public static string SetAddReturnsBool()
    {
        var s = new HashSet<int>();
        return s.Add(1) + "," + s.Add(1) + "," + s.Add(2) + "," + s.Count;
    }

    public static string RecordToString()
    {
        var item = new CeItem("a", 1);
        return item.ToString() + "|" + $"{item}" + "|" + new CeNested(item, null);
    }

    public static string RecordStructToString()
    {
        return new CePoint(1, 2).ToString();
    }

    public static string HashSetComparer()
    {
        var s = new HashSet<string>(new CeLengthComparer()) { "ab", "cd", "xyz" };
        return s.Count + "," + s.Contains("zz") + "," + s.Add("qq");
    }

    public static string DictionaryComparer()
    {
        var d = new Dictionary<string, int>(new CeLengthComparer()) { ["ab"] = 1 };
        d["cd"] = 2;
        return d.Count + "," + d["zz"] + "," + d.ContainsKey("xyz");
    }

    public static string StringComparerKeys()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 };
        d["a"] = 2;
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "x", "X" };
        return d.Count + "," + d["a"] + "," + s.Count;
    }

    public static string DistinctComparer()
    {
        var xs = new[] { "ab", "cd", "xyz", "q" };
        return string.Join(",", xs.Distinct(new CeLengthComparer()));
    }

    public static string NegativeZeroKeys()
    {
        var d = new Dictionary<double, string> { [-0.0] = "neg" };
        var s = new HashSet<double> { -0.0 };
        var keys = string.Join(",", d.Keys.Select(k => 1 / k < 0 ? "-0" : "+0"));
        return keys
            + "|"
            + string.Join(",", s.Select(k => 1 / k < 0 ? "-0" : "+0"))
            + "|"
            + d.ContainsKey(0.0);
    }

    public static string AddRangeSelf()
    {
        var xs = new List<int> { 1, 2, 3 };
        try
        {
            xs.AddRange(xs);
            return string.Join(",", xs);
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    public static string AsListCast()
    {
        object o = new HashSet<int> { 1 };
        try
        {
            var l = (IList<int>)o;
            return "cast" + l.Count;
        }
        catch (InvalidCastException)
        {
            return "ICE";
        }
    }

    public static string ListEquals()
    {
        var a = new List<int> { 1 };
        var b = new List<int> { 1 };
        return a.Equals(a) + "," + a.Equals(b) + "," + a.Equals(null);
    }

    public static string DictionaryViews()
    {
        var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        ICollection<KeyValuePair<string, int>> pairs = d;
        return d.Keys.Count + "," + d.Values.Count + "," + pairs.Count + "," + d.Keys.Contains("a");
    }

    public static string ImmutableArrayEmpty()
    {
        var a = ImmutableArray<int>.Empty;
        var b = a.Add(1).Clear();
        return a.IsEmpty + "," + b.IsEmpty + "," + a.IsDefaultOrEmpty;
    }

    public static string ImmutableListEmptyAddRange()
    {
        var src = ImmutableList.Create(1, 2);
        var r = ImmutableList<int>.Empty.AddRange(src);
        return ReferenceEquals(r, src) + "," + r.Count;
    }

    public static string ImmutableListLastIndexOf()
    {
        var l = ImmutableList.Create(1, 2, 1, 2);
        return l.LastIndexOf(1) + "," + l.LastIndexOf(2, 2, 3);
    }

    public static string SortedSetMinNull()
    {
        var s = new SortedSet<string>(new[] { "b", "a" });
        return s.Min + "," + s.Max;
    }
}
