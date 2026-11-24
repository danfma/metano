namespace Conformance;

public class SePlain
{
    public int V;

    public SePlain(int v) => V = v;
}

public class SeEq
{
    public int V;

    public SeEq(int v) => V = v;

    public override bool Equals(object? obj) => obj is SeEq o && o.V == V;

    public override int GetHashCode() => V;
}

public sealed class SeLengthComparer : IEqualityComparer<string?>
{
    public bool Equals(string? x, string? y) => x == y;

    public int GetHashCode(string? value) => value!.Length;
}

/// <summary>
/// Wave 7 track E (#664): value equality in the LINQ searches. Contains, GroupBy, SequenceEqual,
/// ToLookup and Join compare through EqualityComparer&lt;T&gt;.Default (or the comparer given): a
/// record, a record struct, NaN and a decimal by value, a class without Equals by reference.
/// </summary>
public static class SearchEqualityProbes
{
    static string B(params bool[] values) => string.Join(",", values);

    // The pipe form has no `lookup[key]` (MS0001): read a group by its key instead.
    static int Of(IEnumerable<IGrouping<CeItem, CeItem>> lookup, CeItem key) =>
        lookup.Where(g => g.Key == key).SelectMany(g => g).Count();

    static IEnumerable<T> Group<T>(IEnumerable<IGrouping<string, T>> lookup, string key) =>
        lookup.Where(g => g.Key == key).SelectMany(g => g);

    public static string ContainsLazy()
    {
        IEnumerable<CeItem> r = new List<CeItem> { new("a", 1) }.Where(_ => true);
        IEnumerable<CePoint> s = new List<CePoint> { new(1, 2) }.Where(_ => true);
        IEnumerable<SePlain> p = new List<SePlain> { new(1) }.Where(_ => true);
        IEnumerable<SeEq> e = new List<SeEq> { new(1) }.Where(_ => true);

        return B(
            r.Contains(new("a", 1)),
            s.Contains(new(1, 2)),
            p.Contains(new(1)),
            e.Contains(new(1)),
            r.Contains(new("a", 2))
        );
    }

    public static string ContainsNumbers()
    {
        IEnumerable<double> d = new List<double> { 1.0, double.NaN }.Where(_ => true);
        IEnumerable<decimal> m = new List<decimal> { 1.5m }.Where(_ => true);
        return B(d.Contains(double.NaN), d.Contains(2.0), m.Contains(1.50m), m.Contains(1.51m));
    }

    public static string ContainsComparer()
    {
        IEnumerable<string> words = new List<string> { "ab", "xyz" }.Where(_ => true);
        var same = new CeLengthComparer();

        return B(
            words.Contains("cd", same),
            words.Contains("c", same),
            words.Contains("cd", EqualityComparer<string>.Default)
        );
    }

    public static string GroupByRecords()
    {
        var items = new List<CeItem>
        {
            new("b", 2),
            new("a", 1),
            new("b", 2),
            new("a", 1),
            new("b", 2),
        };

        return string.Join(";", items.GroupBy(x => x).Select(g => g.Key.Name + g.Count()));
    }

    public static string GroupByStructsAndClasses()
    {
        var points = new List<CePoint> { new(1, 2), new(1, 2), new(3, 4) };
        var eq = new List<SeEq> { new(1), new(1) };
        var plain = new List<SePlain> { new(1), new(1) };
        return $"{points.GroupBy(x => x).Count()},{eq.GroupBy(x => x).Count()},{plain.GroupBy(x => x).Count()}";
    }

    public static string GroupByNumbers()
    {
        var doubles = new List<double> { double.NaN, 1.0, double.NaN };
        var decimals = new List<decimal> { 1.5m, 1.50m, 2m };
        return $"{doubles.GroupBy(x => x).Count()},{decimals.GroupBy(x => x).Count()}";
    }

    public static string GroupByComparer()
    {
        var words = new List<string> { "ab", "xyz", "cd", "q", "mno" };

        return string.Join(
            ";",
            words
                .GroupBy(x => x, new CeLengthComparer())
                .Select(g => g.Key + ":" + string.Join(",", g))
        );
    }

    public static string GroupByKeepsFirstKeyAndElementOrder()
    {
        var items = new List<CeItem> { new("a", 1), new("b", 2), new("a", 1) };
        var groups = items.GroupBy(x => x).ToList();
        return $"{ReferenceEquals(groups[0].Key, items[0])},{ReferenceEquals(groups[0].First(), items[0])},{ReferenceEquals(groups[0].Last(), items[2])},{groups[1].Key.Name}";
    }

    public static string SequenceEqualValues()
    {
        var r1 = new List<CeItem> { new("a", 1) };
        var r2 = new List<CeItem> { new("a", 1) };
        var p1 = new List<SePlain> { new(1) };
        var p2 = new List<SePlain> { new(1) };
        var e1 = new List<SeEq> { new(1) };
        var e2 = new List<SeEq> { new(1) };

        return B(
            r1.SequenceEqual(r2),
            p1.SequenceEqual(p2),
            e1.SequenceEqual(e2),
            p1.SequenceEqual(p1)
        );
    }

    public static string SequenceEqualShapes()
    {
        var a = new List<int> { 1, 2 };
        var d = new List<double> { double.NaN, 1.0 };
        var m = new List<decimal> { 1.5m };

        return B(
            a.SequenceEqual(new List<int> { 2, 1 }),
            a.SequenceEqual(new List<int> { 1 }),
            a.SequenceEqual(new List<int> { 1, 2, 3 }),
            new List<int>().SequenceEqual(new List<int>()),
            d.SequenceEqual(new List<double> { double.NaN, 1.0 }),
            m.SequenceEqual(new List<decimal> { 1.50m })
        );
    }

    public static string SequenceEqualComparer()
    {
        var words = new List<string> { "ab", "xyz" };

        return B(
            words.SequenceEqual(new List<string> { "cd", "qrs" }, new CeLengthComparer()),
            words.SequenceEqual(new List<string> { "cd", "q" }, new CeLengthComparer()),
            words.SequenceEqual(new List<string> { "cd", "qrs" })
        );
    }

    public static string ToLookupRecords()
    {
        var items = new List<CeItem> { new("b", 2), new("a", 1), new("b", 2) };
        var lookup = items.ToLookup(x => x);
        return $"{lookup.Count},{Of(lookup, new CeItem("b", 2))},{Of(lookup, new CeItem("z", 9))},{lookup.Contains(new CeItem("a", 1))},{lookup.Contains(new CeItem("a", 9))},{string.Join(";", lookup.Select(g => g.Key.Name + g.Count()))}";
    }

    public static string ToLookupSelectorAndComparer()
    {
        var words = new List<string> { "ab", "xyz", "cd", "q" };
        var byLength = words.ToLookup(x => x, new CeLengthComparer());
        var lengths = words.ToLookup(x => x.Length, x => x + "!");
        var both = words.ToLookup(x => x, x => x.Length, new CeLengthComparer());
        return $"{byLength.Count},{byLength.Contains("zz")},{byLength.Contains("zzz")},{string.Join(",", Group(byLength, "ab"))},{string.Join(",", lengths.Where(g => g.Key == 2).SelectMany(g => g))},{string.Join(",", Group(both, "q"))},{lengths.Count}";
    }

    public static string JoinRecords()
    {
        var outer = new List<CeItem> { new("a", 1), new("b", 2), new("c", 3) };
        var inner = new List<CeItem> { new("b", 2), new("a", 1), new("a", 1) };

        return string.Join(
            ",",
            outer.Join(inner, o => o, i => i, (o, i) => o.Name + inner.IndexOf(i))
        );
    }

    public static string JoinNullKeysAndComparer()
    {
        var outer = new List<string?> { null, "ab", "xyz" };
        var inner = new List<string?> { null, "cd", "qrs", "t" };
        var byValue = outer.Join(inner, o => o, i => i, (o, i) => (o ?? "null") + i);

        var byLength = outer.Join(
            inner,
            o => o,
            i => i,
            (o, i) => (o ?? "null") + i,
            new CeLengthComparer()
        );

        return string.Join(",", byValue) + "|" + string.Join(",", byLength);
    }

    static int _reads;

    static IEnumerable<int> Counted(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _reads++;

            yield return i;
        }
    }

    static IEnumerable<int> Throwing()
    {
        if (_reads >= 0)
            throw new InvalidOperationException();

        yield return 0;
    }

    public static string JoinEmptyInnerCounts()
    {
        _reads = 0;

        var keyCalls = 0;

        var result = Counted(5)
            .Join(
                new List<int>(),
                o =>
                {
                    keyCalls++;

                    return o;
                },
                i => i,
                (o, i) => o + i
            )
            .Count();

        return $"{result},{_reads},{keyCalls}";
    }

    public static string GroupByNullKeyWithComparer()
    {
        var words = new List<string?> { "a", null, "b", null };

        return string.Join(
            ";",
            words.GroupBy(x => x, new SeLengthComparer()).Select(g => (g.Key ?? "null") + g.Count())
        );
    }

    public static string ToLookupNullKeyWithComparer()
    {
        var words = new List<string?> { "a", null, "b", null };
        var lookup = words.ToLookup(x => x, new SeLengthComparer());

        return $"{lookup.Count},{lookup.Contains(null)},{lookup.Contains("a")}";
    }

    public static string GroupByReadsOnFirstMoveNext()
    {
        _reads = 0;

        try
        {
            return Throwing()
                .SequenceEqual(Counted(3).GroupBy(x => x).Select(g => g.Key))
                .ToString();
        }
        catch (InvalidOperationException)
        {
            return "threw," + _reads;
        }
    }
}
