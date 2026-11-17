namespace Conformance;

public static class LinqSetProbes
{
    static string N(object? o) => o == null ? "null" : o.ToString()!;

    public static string SetOps()
    {
        var a = new[] { 1, 2, 2, 3 };
        var b = new[] { 2, 4 };
        return string.Join(",", a.Union(b))
            + "|"
            + string.Join(",", a.Intersect(b))
            + "|"
            + string.Join(",", a.Except(b))
            + "|"
            + string.Join(",", a.Distinct())
            + "|"
            + string.Join(",", a.Concat(b))
            + "|"
            + a.Union(b).Count()
            + "|"
            + string.Join(",", new List<string> { "a", "b" }.Union(new[] { "b", "c" }))
            + "|"
            + string.Join(",", new[] { "a", "A" }.Union(new[] { "a" }))
            + "|"
            + string.Join(",", a.Except(a));
    }

    public static string SetOpsRecords()
    {
        var a = new[] { new Inner("x"), new Inner("y") };
        var b = new[] { new Inner("y"), new Inner("z") };
        return a.Union(b).Count()
            + "|"
            + a.Intersect(b).Count()
            + "|"
            + a.Except(b).Count()
            + "|"
            + a.Distinct().Count()
            + "|"
            + new[] { 1.5m, 1.50m }.Union(new[] { 1.500m }).Count()
            + "|"
            + new[] { 1.5m }.Intersect(new[] { 1.50m }).Count()
            + "|"
            + new[] { (1, "a") }.Union(new[] { (1, "a") }).Count();
    }

    public static string OnOtherSources()
    {
        var h = new HashSet<int> { 1, 2 };
        var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var s = "hello";
        return h.Max()
            + "|"
            + h.Sum()
            + "|"
            + d.Values.Max()
            + "|"
            + d.Keys.Min()
            + "|"
            + d.Sum(kv => kv.Value)
            + "|"
            + d.Max(kv => kv.Value)
            + "|"
            + s.Max()
            + "|"
            + s.Count(c => c == 'l')
            + "|"
            + s.Distinct().Count()
            + "|"
            + d.Where(kv => kv.Value > 1).Select(kv => kv.Key).First()
            + "|"
            + h.Union(new[] { 3 }).Count()
            + "|"
            + d.Keys.Union(new[] { "c" }).Count()
            + "|"
            + d.Count()
            + "|"
            + h.Count();
    }

    public static string Comparers()
    {
        var xs = new[] { "b", "A", "c" };
        return string.Join(",", xs.OrderBy(x => x, StringComparer.Ordinal))
            + "|"
            + string.Join(",", xs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            + "|"
            + xs.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            + "|"
            + xs.Contains("a", StringComparer.OrdinalIgnoreCase)
            + "|"
            + string.Join(",", xs.Union(new[] { "B" }, StringComparer.OrdinalIgnoreCase));
    }
}
