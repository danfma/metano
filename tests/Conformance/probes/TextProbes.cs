namespace Conformance;

public record Flags(bool On, double Ratio, float F, bool? Maybe);

public static class TextProbes
{
    public static string BoolConcat()
    {
        bool t = true,
            f = false;
        return "a"
            + t
            + f
            + "|"
            + t.ToString()
            + f.ToString()
            + "|"
            + $"{t}{f}"
            + "|"
            + string.Join(",", t, f);
    }

    public static string BoolNullable()
    {
        bool? n = null;
        bool? t = true;
        return "a" + n + t + "|" + $"{n}{t}" + "|" + (n?.ToString() ?? "null") + t.ToString() + "|";
    }

    public static string BoolExpr()
    {
        int a = 1;
        return "r=" + (a > 0) + (a == 2) + $"{a > 0}" + (a > 0 && true).ToString() + (!true);
    }

    public static string BoolAligned()
    {
        return $"[{true, 6}][{false, -6}]";
    }

    public static string Doubles()
    {
        double a = 1e-7,
            b = 1e21,
            c = 0.1 + 0.2,
            d = 1.0,
            e = -0.0,
            f = 123456789012345680000.0,
            g = 1e15,
            h = 1e16,
            i = 1.5e-5,
            j = 0.0001;
        return a
            + "|"
            + b
            + "|"
            + c
            + "|"
            + d
            + "|"
            + e
            + "|"
            + f
            + "|"
            + g
            + "|"
            + h
            + "|"
            + i
            + "|"
            + j;
    }

    public static string DoubleSpecial()
    {
        return double.NaN
            + "|"
            + double.PositiveInfinity
            + "|"
            + double.NegativeInfinity
            + "|"
            + double.MaxValue
            + "|"
            + double.Epsilon
            + "|"
            + double.MinValue;
    }

    public static string DoubleInterp()
    {
        double a = 1e-7,
            b = 1e21;
        double? n = null;
        return $"{a}|{b}|{n}|{a, 10}|{1.5:F2}|{b:E2}"
            + "|"
            + a.ToString()
            + "|"
            + string.Join(",", a, b);
    }

    public static string Floats()
    {
        float a = 0.1f,
            b = 1e-7f,
            c = 16777216f,
            d = 3.4028235e38f,
            e = 1.5f,
            f = 1f / 3f;
        return a
            + "|"
            + b
            + "|"
            + c
            + "|"
            + d
            + "|"
            + e
            + "|"
            + f
            + "|"
            + a.ToString()
            + "|"
            + $"{a}{f}"
            + "|"
            + float.NaN
            + "|"
            + (a + 0.2f)
            + "|"
            + (double)a;
    }

    public static string FloatMath()
    {
        float a = 0.1f,
            b = 0.2f;
        var c = a + b;
        double d = a;
        return c
            + "|"
            + d
            + "|"
            + (c == 0.3f ? "T" : "F")
            + "|"
            + (a * 3f)
            + "|"
            + (float)0.1
            + "|"
            + (float)(0.1 + 0.2);
    }

    public static string JoinArrays()
    {
        var bs = new[] { true, false };
        var ds = new[] { 1e-7, 2.0, 1e21 };
        var fs = new[] { 0.1f };
        var os = new object[] { true, 1e-7, 1, "s" };
        return string.Join(",", bs)
            + "|"
            + string.Join(",", ds)
            + "|"
            + string.Join(",", fs)
            + "|"
            + string.Join(",", os)
            + "|"
            + string.Join(",", new List<bool> { true })
            + "|"
            + string.Join(",", ds.Select(x => x * 2));
    }

    public static string Boxed()
    {
        object b = true;
        object d = 1e-7;
        return b + "|" + d + "|" + b.ToString() + "|" + $"{b}{d}";
    }

    public static string RecordToString()
    {
        return new Flags(true, 1e-7, 0.1f, null).ToString();
    }

    public static string TupleToString()
    {
        return (true, 1e-7).ToString() + "|" + (1, "a").ToString();
    }

    public static string Compound()
    {
        var s = "v:";
        s += true;
        s += 1e-7;
        s += 0.1f;
        s += 5;
        s += 'c';
        s += 2.50m;
        s += 3L;
        return s;
    }

    public static string Generic()
    {
        return G(true) + G(1e-7) + G(0.1f) + G(1) + G<bool?>(null);
    }

    static string G<T>(T x) => "<" + x + ">" + $"{x}" + x?.ToString();

    public static string Ints()
    {
        int i = -5;
        long l = 9007199254740993L;
        uint u = 4000000000;
        byte b = 255;
        short s = -3;
        ulong ul = ulong.MaxValue;
        return i
            + "|"
            + l
            + "|"
            + u
            + "|"
            + b
            + "|"
            + s
            + "|"
            + ul
            + "|"
            + $"{i}{l}"
            + "|"
            + i.ToString()
            + l.ToString();
    }

    public static string Chars()
    {
        char c = 'a';
        return c
            + "b"
            + "|"
            + (c + 1)
            + "|"
            + (char)(c + 1)
            + "|"
            + c.ToString()
            + "|"
            + $"{c}"
            + "|"
            + ("" + c + c)
            + "|"
            + (c + c);
    }

    public static string Dates()
    {
        var d = new DateOnly(2024, 3, 5);
        var t = new TimeSpan(1, 2, 3);
        var g = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        return d.ToString("yyyy-MM-dd")
            + "|"
            + g
            + "|"
            + $"{g}"
            + "|"
            + $"{t}"
            + "|"
            + t.ToString()
            + "|"
            + TimeSpan.FromMilliseconds(1500).ToString()
            + "|"
            + TimeSpan.FromDays(1.5).ToString();
    }

    public static string EnumsText()
    {
        var c = Color.Green;
        Color? n = null;
        return c
            + "|"
            + $"{c}"
            + "|"
            + c.ToString()
            + "|"
            + n
            + "|"
            + (Color)7
            + "|"
            + Mode.Slow
            + "|"
            + $"{Mode.Fast}";
    }

    public static string Conditional()
    {
        bool? b = true;
        double? d = 1e-7;
        return (b?.ToString() ?? "n")
            + "|"
            + (d?.ToString() ?? "n")
            + "|"
            + b
            + d
            + "|"
            + (b == true ? "y" : "n")
            + "|"
            + b.Value
            + d.Value;
    }

    public static string Equality()
    {
        double a = 0.1 + 0.2;
        return (a == 0.3) + "|" + (double.NaN == double.NaN);
    }

    public static string ToStringCalls()
    {
        return true.ToString().ToLower()
            + "|"
            + 1.5.ToString()
            + "|"
            + 1e-7.ToString().Length
            + "|"
            + (1.0 / 3).ToString()
            + "|"
            + 100.0.ToString()
            + "|"
            + 1e100.ToString()
            + "|"
            + (0.1f).ToString()
            + "|"
            + 1.5.ToString("F3")
            + "|"
            + 1234.5.ToString("N1")
            + "|"
            + 0.5.ToString("P0")
            + "|"
            + 255.ToString("X")
            + "|"
            + 5.ToString("D3")
            + "|"
            + 1.5.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
