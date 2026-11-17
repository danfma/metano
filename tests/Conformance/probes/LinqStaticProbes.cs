namespace Conformance;

public static class LinqStaticProbes
{
    public static string StaticForms()
    {
        var xs = new[] { 3, 1, 2 };
        return Enumerable.Count(xs)
            + "|"
            + Enumerable.Sum(xs)
            + "|"
            + Enumerable.First(xs)
            + "|"
            + string.Join(",", Enumerable.Where(xs, x => x > 1))
            + "|"
            + string.Join(",", Enumerable.Select(xs, x => x * 2))
            + "|"
            + Enumerable.Any(xs)
            + "|"
            + string.Join(",", Enumerable.OrderBy(xs, x => x))
            + "|"
            + string.Join(",", Enumerable.Range(1, 3))
            + "|"
            + Enumerable.Max(xs)
            + "|"
            + string.Join(",", Enumerable.Union(xs, new[] { 9 }))
            + "|"
            + Enumerable.Contains(xs, 2)
            + "|"
            + Enumerable.ToList(xs).Count;
    }
}
