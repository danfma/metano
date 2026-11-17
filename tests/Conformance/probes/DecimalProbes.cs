namespace Conformance;

public record Price(decimal Amount, string Cur);

public static class DecimalProbes
{
    static string T(object o) =>
        o is decimal ? "dec"
        : o is double ? "dbl"
        : o is int ? "int"
        : "other";

    public static string Arithmetic()
    {
        decimal a = 1.10m,
            b = 2.205m;
        return (a + b) + "|" + (a * b) + "|" + (b - a) + "|" + (b / a) + "|" + (b % a) + "|" + (-a);
    }

    public static string Thirds()
    {
        return (1m / 3m) + "|" + (2m / 3m) + "|" + (10m / 4m) + "|" + (1m / 3m * 3m);
    }

    public static string TrailingZeros()
    {
        return 1.50m
            + "|"
            + (1.50m + 1.50m)
            + "|"
            + (1.0m * 1.00m)
            + "|"
            + 100m
            + "|"
            + decimal.Parse("2.500");
    }

    public static string Compare()
    {
        decimal a = 1.0m,
            b = 1.00m;
        return (a == b ? "T" : "F")
            + (a.Equals(b) ? "T" : "F")
            + (a < 2m ? "T" : "F")
            + (a.CompareTo(b))
            + (a >= b ? "T" : "F")
            + (a != 1.5m ? "T" : "F");
    }

    public static string Rounding()
    {
        return Math.Round(2.5m)
            + "|"
            + Math.Round(3.5m)
            + "|"
            + Math.Round(2.345m, 2)
            + "|"
            + Math.Round(2.355m, 2)
            + "|"
            + Math.Round(2.5m, MidpointRounding.AwayFromZero)
            + "|"
            + Math.Floor(-1.5m)
            + "|"
            + Math.Ceiling(1.2m)
            + "|"
            + Math.Truncate(-1.7m)
            + "|"
            + decimal.Round(1.005m, 2);
    }

    public static string MathOps()
    {
        return Math.Abs(-1.5m)
            + "|"
            + Math.Max(1m, 2m)
            + "|"
            + Math.Min(1m, 2m)
            + "|"
            + Math.Sign(-2m);
    }

    public static string Conversions()
    {
        int i = 3;
        long l = 4L;
        double d = 1.5;
        decimal fromI = i;
        decimal fromL = l;
        decimal fromD = (decimal)d;
        return fromI
            + "|"
            + fromL
            + "|"
            + fromD
            + "|"
            + (int)2.9m
            + "|"
            + (double)1.25m
            + "|"
            + (long)7.9m
            + "|"
            + (int)-2.9m;
    }

    public static string TypeTests()
    {
        object o = 1.5m;
        object p = 1.5;
        object q = 1;
        return T(o) + T(p) + T(q) + (o is decimal m && m > 1m ? "T" : "F");
    }

    public static string Big()
    {
        decimal a = 79228162514264337593543950335m;
        decimal b = 0.0000000000000000000000000001m;
        return a
            + "|"
            + b
            + "|"
            + (a - 1m)
            + "|"
            + (b * 1m)
            + "|"
            + (12345678901234567890.123456789m * 2m);
    }

    public static string Precision()
    {
        return (0.1m + 0.2m)
            + "|"
            + (0.1m + 0.2m == 0.3m ? "T" : "F")
            + "|"
            + (1.23456789012345678901234567m * 1.1m)
            + "|"
            + (1m / 7m);
    }

    public static string ToStringFormats()
    {
        decimal a = 1234.5m;
        return a.ToString() + "|" + $"{a}";
    }

    public static string ParseTry()
    {
        var ok = decimal.TryParse("12.50", out var v);
        var bad = decimal.TryParse("x", out var w);
        var e = decimal.TryParse("", out var z);
        var sp = decimal.TryParse(" 1,234.5 ", out var t);
        return ok + "|" + v + "|" + bad + "|" + w + "|" + e + "|" + sp + "|" + t;
    }

    public static string Records()
    {
        var a = new Price(1.10m, "USD");
        var b = new Price(1.1m, "USD");
        return (a == b ? "T" : "F")
            + "|"
            + a.Amount
            + "|"
            + (a with { Amount = a.Amount * 2 }).Amount
            + "|"
            + a;
    }

    public static string Collections()
    {
        var xs = new List<decimal> { 1.5m, 2.5m, 3m };
        return xs.Sum()
            + "|"
            + xs.Average()
            + "|"
            + xs.Max()
            + "|"
            + xs.Min()
            + "|"
            + xs.Contains(2.50m)
            + "|"
            + xs.IndexOf(3.0m)
            + "|"
            + xs.Where(x => x > 2m).Count()
            + "|"
            + string.Join(",", xs.OrderByDescending(x => x))
            + "|"
            + xs.Select(x => x * 2).Sum()
            + "|"
            + xs.Sum(x => x);
    }

    public static string DictKey()
    {
        var d = new Dictionary<decimal, string> { [1.0m] = "one" };
        var h = new HashSet<decimal> { 1.0m, 1.00m, 2m };
        return (d.ContainsKey(1.00m) ? "T" : "F")
            + h.Count
            + (d.TryGetValue(1m, out var v) ? v : "miss");
    }

    public static string Compound()
    {
        decimal a = 1m;
        a += 2.5m;
        a *= 2;
        a -= 1;
        a /= 3;
        a++;
        return a.ToString();
    }

    public static string Constants()
    {
        return decimal.Zero + "|" + decimal.One + "|" + decimal.MinusOne;
    }

    public static string Overflow()
    {
        try
        {
            var a = decimal.MaxValue;
            var b = a + 1m;
            return "no:" + b;
        }
        catch (OverflowException)
        {
            return "overflow";
        }
    }

    public static string DivZero()
    {
        try
        {
            decimal z = 0m;
            var b = 1m / z;
            return "no:" + b;
        }
        catch (DivideByZeroException)
        {
            return "dbz";
        }
    }

    public static string Nullable()
    {
        decimal? a = null;
        decimal? b = 1.5m;
        return (a ?? 0m)
            + "|"
            + (a + b == null ? "null" : "x")
            + "|"
            + (b + 1m)
            + "|"
            + (a.HasValue ? "T" : "F")
            + "|"
            + b.Value
            + "|"
            + (b > 1m ? "T" : "F")
            + (a > 1m ? "T" : "F");
    }

    public static string AverageThirds()
    {
        var xs = new[] { 1m, 1m, 2m };
        return xs.Average()
            + "|"
            + new[] { 1m, 2m }.Average()
            + "|"
            + new decimal[] { 10m, 20m, 40m }.Average(x => x / 3m);
    }

    public static string Negatives()
    {
        return (-0.0m).ToString()
            + "|"
            + (0m * -1m)
            + "|"
            + (-1.50m)
            + "|"
            + (1m - 1.0m)
            + "|"
            + 0.00m;
    }
}
