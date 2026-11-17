namespace Conformance;

public record Item(string Name, int Qty, decimal Price, double? Weight);

public static class LinqProbes
{
    static string P(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Exception e)
        {
            return ProbeUtil.Kind(e) + ":" + e.Message;
        }
    }

    static string N(object? o) => o == null ? "null" : o.ToString()!;

    static readonly List<Item> Items = new()
    {
        new("a", 2, 1.5m, 0.5),
        new("b", 5, 2.25m, null),
        new("c", 1, 10m, 2.5),
    };
    static readonly List<Item> None = new();

    public static string MinMaxInts()
    {
        var xs = new[] { 3, 1, 2 };
        return xs.Min()
            + "|"
            + xs.Max()
            + "|"
            + xs.Min(x => -x)
            + "|"
            + xs.Max(x => x * 2)
            + "|"
            + Items.Min(i => i.Qty)
            + "|"
            + Items.Max(i => i.Qty);
    }

    public static string MinMaxEmpty() =>
        P(() =>
            new int[0]
                .Min()
                .ToString()
        )
        + "|"
        + P(() =>
            new int[0]
                .Max()
                .ToString()
        )
        + "|"
        + P(() => None.Min(i => i.Qty).ToString())
        + "|"
        + P(() => None.Max(i => i.Price).ToString())
        + "|"
        + P(() =>
            new double[0]
                .Min()
                .ToString()
        )
        + "|"
        + P(() => new string[0].Min() ?? "null")
        + "|"
        + P(() =>
            new decimal[0]
                .Max()
                .ToString()
        )
        + "|"
        + P(() =>
            new long[0]
                .Min()
                .ToString()
        );

    public static string MinMaxNullable()
    {
        var xs = new int?[] { 3, null, 1 };
        var e = new int?[0];
        var allNull = new int?[] { null };
        return N(xs.Min())
            + "|"
            + N(xs.Max())
            + "|"
            + N(e.Min())
            + "|"
            + N(e.Max())
            + "|"
            + N(allNull.Min())
            + "|"
            + N(Items.Min(i => i.Weight))
            + "|"
            + N(Items.Max(i => i.Weight))
            + "|"
            + N(None.Max(i => i.Weight))
            + "|"
            + N(xs.Sum())
            + "|"
            + N(xs.Average())
            + "|"
            + N(e.Sum())
            + "|"
            + N(e.Average());
    }

    public static string MinMaxDecimal()
    {
        var xs = new[] { 1.5m, 0.5m, 2m };
        return xs.Min()
            + "|"
            + xs.Max()
            + "|"
            + Items.Min(i => i.Price)
            + "|"
            + Items.Max(i => i.Price)
            + "|"
            + Items.Sum(i => i.Price)
            + "|"
            + Items.Average(i => i.Price)
            + "|"
            + Items.Sum(i => i.Price * i.Qty)
            + "|"
            + xs.Sum()
            + "|"
            + xs.Average();
    }

    public static string MinMaxStrings()
    {
        var xs = new[] { "b", "a", "C" };
        return xs.Min()
            + "|"
            + xs.Max()
            + "|"
            + Items.Min(i => i.Name)
            + "|"
            + Items.Max(i => i.Name)
            + "|"
            + new[] { "b", null, "a" }.Min()
            + "|"
            + new[] { 'x', 'a' }.Max();
    }

    public static string MinMaxDoubles()
    {
        var xs = new[] { 1.5, double.NaN, 0.5 };
        return xs.Min().ToString()
            + "|"
            + xs.Max().ToString()
            + "|"
            + new[] { 2.0, 1.0 }.Min().ToString()
            + "|"
            + new[] { 1.5f, 2.5f }.Max().ToString()
            + "|"
            + new[] { 5L, 7L }.Max();
    }

    public static string MinMaxBy()
    {
        return Items.MinBy(i => i.Qty)!.Name
            + "|"
            + Items.MaxBy(i => i.Price)!.Name
            + "|"
            + N(None.MinBy(i => i.Qty))
            + "|"
            + N(None.MaxBy(i => i.Qty))
            + "|"
            + P(() =>
                new int[0]
                    .MinBy(x => x)
                    .ToString()
            )
            + "|"
            + new[] { 3, 1, 2 }.MaxBy(x => -x)
            + "|"
            + Items.MaxBy(i => i.Weight)!.Name
            + "|"
            + Items.MinBy(i => i.Weight)!.Name;
    }

    public static string SumAverage()
    {
        var xs = new[] { 1, 2, 4 };
        var e = new int[0];
        return xs.Sum()
            + "|"
            + xs.Average().ToString()
            + "|"
            + e.Sum()
            + "|"
            + P(() => e.Average().ToString())
            + "|"
            + xs.Sum(x => x * 1.5).ToString()
            + "|"
            + new[] { 1L, 2L }.Sum()
            + "|"
            + new[] { 1L, 2L }.Average().ToString()
            + "|"
            + new[] { 1.5, 2.5 }.Average().ToString()
            + "|"
            + new[] { 0.1f, 0.2f }.Sum().ToString()
            + "|"
            + Items.Average(i => i.Qty).ToString()
            + "|"
            + P(() => None.Average(i => i.Price).ToString())
            + "|"
            + None.Sum(i => i.Price)
            + "|"
            + new decimal[0].Sum();
    }

    public static string Ordered()
    {
        var o = Items.OrderBy(i => i.Qty);
        return o.Min(i => i.Price)
            + "|"
            + o.Max(i => i.Qty)
            + "|"
            + o.First().Name
            + "|"
            + o.Last().Name
            + "|"
            + Items.OrderByDescending(i => i.Price).ThenBy(i => i.Name).Select(i => i.Name).Min()
            + "|"
            + o.Sum(i => i.Qty)
            + "|"
            + o.MinBy(i => i.Price)!.Name
            + "|"
            + o.Count();
    }

    public static string Basics()
    {
        var xs = new List<int> { 1, 2, 3, 4 };
        return xs.Where(x => x % 2 == 0).Count()
            + "|"
            + xs.Any(x => x > 3)
            + "|"
            + xs.All(x => x > 0)
            + "|"
            + xs.First()
            + "|"
            + xs.Last()
            + "|"
            + xs.FirstOrDefault(x => x > 9)
            + "|"
            + xs.Single(x => x == 2)
            + "|"
            + xs.Skip(1).Take(2).Sum()
            + "|"
            + xs.Contains(3)
            + "|"
            + xs.Count()
            + "|"
            + xs.Reverse<int>().First()
            + "|"
            + xs.ToArray().Length
            + "|"
            + xs.Aggregate(0, (a, b) => a + b)
            + "|"
            + string.Join(",", xs.Select((x, i) => x * i))
            + "|"
            + xs.ToDictionary(x => x, x => x * 2)[2]
            + "|"
            + xs.GroupBy(x => x % 2).Count()
            + "|"
            + xs.SelectMany(x => new[] { x, x }).Count()
            + "|"
            + xs.Zip(xs, (a, b) => a + b).Last()
            + "|"
            + xs.LastOrDefault(x => x < 0)
            + "|"
            + xs.SingleOrDefault(x => x > 9)
            + "|"
            + xs.Count(x => x > 1)
            + "|"
            + xs.ToHashSet().Count
            + "|"
            + xs.Append(5).Prepend(0).Count()
            + "|"
            + xs.TakeWhile(x => x < 3).Count()
            + "|"
            + xs.SkipWhile(x => x < 3).Count();
    }

    public static string BasicsThrow() =>
        P(() =>
            new int[0]
                .First()
                .ToString()
        )
        + "|"
        + P(() =>
            new int[0]
                .Single()
                .ToString()
        )
        + "|"
        + P(() => new[] { 1, 2 }.Single().ToString())
        + "|"
        + P(() =>
            new int[0]
                .Last()
                .ToString()
        )
        + "|"
        + P(() => new[] { 1, 2 }.First(x => x > 5).ToString());

    public static string DecimalLambda()
    {
        Func<decimal, decimal> dbl = x => x * 2;
        var xs = new[] { 1.5m, 2m };
        return dbl(1.5m)
            + "|"
            + xs.Select(x => x + 1).Sum()
            + "|"
            + xs.Aggregate(0m, (a, x) => a + x)
            + "|"
            + xs.Where(x => x > 1.5m).Count()
            + "|"
            + xs.Sum(x => x * 2)
            + "|"
            + xs.Select(x => x.ToString()).First()
            + "|"
            + xs.OrderBy(x => -x).First()
            + "|"
            + xs.Max(x => x * 3)
            + "|"
            + xs.Average(x => x / 2);
    }

    public static string IntOverflowSum() =>
        P(() => new[] { int.MaxValue, 1 }.Sum().ToString())
        + "|"
        + P(() => new[] { long.MaxValue, 1L }.Sum().ToString())
        + "|"
        + new[] { int.MaxValue, 1 }.Average().ToString()
        + "|"
        + new[] { 1, 2 }.Average().ToString();
}
