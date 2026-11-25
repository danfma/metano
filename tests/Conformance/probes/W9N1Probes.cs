namespace Conformance;

/// <summary>
/// Wave 9 group N1: HashCode.Combine (#749), Math.Clamp (#750), DateTimeKind (#753) and
/// ValueTuple value equality in runtime collections (#746).
/// </summary>
public static class W9N1Probes
{
    static string Safe<T>(Func<T> body)
    {
        try
        {
            return "" + body();
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ":" + exception.Message.Replace("\n", "|");
        }
    }

    // .NET seeds HashCode randomly per process, so only equality properties are comparable.
    public static string HashCodeCombine()
    {
        var a = HashCode.Combine(1, "x");
        var b = HashCode.Combine(1, "x");
        var c = HashCode.Combine(2, "x");
        var d = HashCode.Combine(1, 2, 3, 4, 5);
        var e = HashCode.Combine(1, 2, 3, 4, 5);

        return (a == b)
            + ","
            + (a != c)
            + ","
            + (d == e)
            + ","
            + (HashCode.Combine(7) == HashCode.Combine(7));
    }

    public static string HashCodeAdd()
    {
        var first = new HashCode();
        first.Add(3);
        first.Add("k");

        var second = new HashCode();
        second.Add(3);
        second.Add("k");

        return "" + (first.ToHashCode() == second.ToHashCode());
    }

    public static string ClampInt() =>
        Math.Clamp(15, 0, 10) + "," + Math.Clamp(-5, 0, 10) + "," + Math.Clamp(5, 0, 10);

    public static string ClampDouble() =>
        Math.Clamp(1.5, 0.0, 1.0)
        + ","
        + Math.Clamp(-0.5, 0.0, 1.0)
        + ","
        + Math.Clamp(0.25, 0.0, 1.0);

    public static string ClampLong() => Math.Clamp(15L, 0L, 10L) + "," + Math.Clamp(-5L, -3L, 10L);

    public static string ClampDecimal() =>
        Math.Clamp(1.5m, 0m, 1m)
        + ","
        + Math.Clamp(-0.5m, 0m, 1m)
        + ","
        + Math.Clamp(0.25m, 0m, 1m);

    public static string ClampByte() => Math.Clamp((byte)200, (byte)1, (byte)100) + "";

    public static string ClampInvalidInt() => Safe(() => Math.Clamp(1, 5, 2));

    public static string ClampInvalidDouble() => Safe(() => Math.Clamp(1.0, 5.0, 2.0));

    public static string ClampInvalidDecimal() => Safe(() => Math.Clamp(1m, 5m, 2m));

    public static string DateTimeUtcKind()
    {
        var value = new DateTime(2024, 3, 5, 10, 20, 30, DateTimeKind.Utc);

        return value.Year
            + ","
            + value.Month
            + ","
            + value.Hour
            + ","
            + value.Second
            + ","
            + value.Millisecond;
    }

    public static string DateTimeUtcKindMillis()
    {
        var value = new DateTime(2024, 3, 5, 10, 20, 30, 40, DateTimeKind.Utc);

        return value.Minute + "," + value.Millisecond;
    }

    public static string TupleHashSet()
    {
        var set = new HashSet<(int, int)> { (1, 2), (1, 2), (2, 1) };

        return set.Count + "," + set.Contains((1, 2)) + "," + set.Contains((2, 2));
    }

    public static string TupleDictionary()
    {
        var map = new Dictionary<(int, string), int> { [(1, "a")] = 10 };
        map[(1, "a")] = 11;

        return map.Count + "," + map[(1, "a")] + "," + map.ContainsKey((2, "a"));
    }

    public static string TupleLinq()
    {
        var items = new List<(int, int)> { (1, 2), (1, 2), (3, 4) };
        var distinct = items.Distinct().Count();
        var groups = items.GroupBy(item => item).Count();

        return distinct + "," + groups + "," + items.Contains((3, 4));
    }

    public static string TupleListIndexOf()
    {
        var items = new List<(int, string)> { (1, "a"), (2, "b") };

        return items.IndexOf((2, "b")) + "," + items.Contains((9, "z"));
    }

    public static string TupleEquals()
    {
        var left = (1, "a");
        var right = (1, "a");

        return left.Equals(right)
            + ","
            + (left == right)
            + ","
            + (left.GetHashCode() == right.GetHashCode());
    }
}
