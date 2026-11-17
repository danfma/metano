namespace Conformance;

public static class IndexProbes
{
    static string N(object? o) => o == null ? "null" : o.ToString()!;

    public static string ReadWrite()
    {
        var a = new int[3];
        a[0] = 5;
        a[1] += 2;
        a[2]++;
        ++a[2];
        a[0] -= 1;
        a[1] *= 3;
        return string.Join(",", a);
    }

    public static string NullableElems()
    {
        var a = new string?[3];
        a[0] = "x";
        a[1] ??= "y";
        a[0] += "z";
        a[2] ??= a[1];
        var n = new int?[2];
        n[0] = 1;
        n[0]++;
        n[1] ??= 7;
        n[0] += 2;
        return string.Join(",", a) + "|" + N(n[0]) + N(n[1]) + "|" + (a[0]?.Length ?? -1);
    }

    public static string Lists()
    {
        var l = new List<int> { 1, 2 };
        l[0] = 9;
        l[1] += 1;
        l[0]++;
        var s = new List<string?> { null, "a" };
        s[0] ??= "n";
        s[1] += "b";
        return string.Join(",", l) + "|" + string.Join(",", s) + "|" + l[^1] + "|" + l[1];
    }

    public static string Dicts()
    {
        var d = new Dictionary<string, int> { ["a"] = 1 };
        d["a"] = d["a"] + 2;
        d["b"] = 5;
        var e = new Dictionary<string, string?> { ["k"] = null };
        e["k"] = e["k"] ?? "v";
        var o = new Dictionary<int, List<int>> { [1] = new() };
        o[1].Add(4);
        return d["a"] + "|" + d["b"] + "|" + e["k"] + "|" + o[1][0] + "|" + o[1].Count;
    }

    public static string Swap()
    {
        var a = new[] { 1, 2, 3 };
        (a[0], a[2]) = (a[2], a[0]);
        var t = a[1];
        a[1] = a[0];
        a[0] = t;
        return string.Join(",", a);
    }

    public static string Nested()
    {
        var m = new int[2][];
        m[0] = new[] { 1, 2 };
        m[1] = new int[2];
        m[1][0] = m[0][1] + 1;
        m[0][0]++;
        var g = new List<List<string>> { new() { "a" } };
        g[0][0] += "b";
        return m[0][0] + "," + m[1][0] + "," + m[1][1] + "|" + g[0][0];
    }

    public static string OutOfRange()
    {
        var a = new[] { 1 };
        try
        {
            return a[3].ToString();
        }
        catch (IndexOutOfRangeException)
        {
            return "ioor";
        }
    }

    public static string OutOfRangeNullable()
    {
        var a = new string?[] { "x" };
        try
        {
            return a[3] ?? "wasnull";
        }
        catch (IndexOutOfRangeException)
        {
            return "ioor";
        }
    }

    public static string ListOutOfRange()
    {
        var a = new List<int> { 1 };
        try
        {
            return a[3].ToString();
        }
        catch (ArgumentOutOfRangeException)
        {
            return "aoor";
        }
    }

    public static string StringsIdx()
    {
        var s = "abc";
        return s[0]
            + ""
            + s[^1]
            + s[1..]
            + s[..1]
            + s[1..2]
            + (s[1] == 'b' ? "T" : "F")
            + (char)(s[0] + 1);
    }

    public static string Ranges()
    {
        var a = new[] { 1, 2, 3, 4 };
        return string.Join(",", a[1..3])
            + "|"
            + string.Join(",", a[..^1])
            + "|"
            + a[^2]
            + "|"
            + a[a.Length - 1];
    }

    public static string Members()
    {
        var a = new[] { "ab", "cde" };
        var p = new[] { new Pt(1, 2) };
        var r = new[] { new Inner("q") };
        var nr = new Inner?[] { null, new Inner("z") };
        return a[1].Length
            + "|"
            + a[0].ToUpper()
            + "|"
            + p[0].X
            + "|"
            + r[0].S
            + "|"
            + (nr[0]?.S ?? "none")
            + "|"
            + nr[1]!.S
            + "|"
            + (nr[0] == null ? "T" : "F")
            + "|"
            + (nr[1] is { S: "z" } ? "T" : "F");
    }

    public static string Loops()
    {
        var a = new[] { 1, 2, 3 };
        var s = 0;
        for (var i = 0; i < a.Length; i++)
        {
            s += a[i];
            a[i] = a[i] * 2;
        }
        var b = new bool[2];
        b[1] = !b[0];
        var d = new double[2];
        d[0] += 1.5;
        d[1] = d[0] / 2;
        return s + "|" + string.Join(",", a) + "|" + (b[1] ? "T" : "F") + "|" + d[1].ToString();
    }

    public static string RefArgs()
    {
        var a = new[] { 1, 2 };
        Bump(ref a[0]);
        int.TryParse("7", out a[1]);
        return string.Join(",", a);
    }

    static void Bump(ref int x)
    {
        x += 10;
    }

    public static string StructElems()
    {
        var v = new Vec[2];
        v[0].X = 3;
        v[1] = v[0];
        v[1].X++;
        return v[0].X + "|" + v[1].X;
    }

    public static string TupleElems()
    {
        var t = new (int A, string B)[] { (1, "x") };
        var (a, b) = t[0];
        return a + b + t[0].A;
    }

    public static string Spans()
    {
        var a = new[] { 1, 2, 3 };
        var l = new List<int>(a);
        var ro = (IReadOnlyList<int>)l;
        IList<int> il = l;
        il[0] = 8;
        return ro[0] + "|" + il[1] + "|" + a[0];
    }

    public static string LongElems()
    {
        var a = new long[2];
        a[0] = 5;
        a[1] += a[0] * 2;
        a[0]++;
        return a[0] + "|" + a[1];
    }

    public static string DecimalElems()
    {
        var a = new decimal[2];
        a[0] = 1.5m;
        a[1] += a[0] * 2;
        return a[0] + "|" + a[1];
    }
}
