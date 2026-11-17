namespace Conformance;

public enum Level
{
    Low = 1,
    High = 2,
}

public enum Zeroed
{
    None,
    Some,
}

[Metano.Annotations.StringEnum]
public enum Mode
{
    Fast,
    Slow,
}

public struct Vec
{
    public int X;
    public int Y;
    public string? Tag;

    public override string ToString() => X + "," + Y + "," + (Tag ?? "null");
}

public record struct RVec(int X, double Y);

public class Defaults
{
    public Level L;
    public Zeroed Z;
    public Mode M;
    public Level? NL;
    public int I;
    public string? S;
    public bool B;
    public double D;
    public long Lg;
    public decimal Dec;
    public Vec V;
    public DateTime Dt;
    public Guid G;
    public char C;
    public TimeSpan Ts;
    public Level P { get; set; }
    public Zeroed Q { get; set; }
    public static Level SL;
}

public static class DefaultProbes
{
    static string N(object? o) => o == null ? "null" : o.ToString()!;

    static T Def<T>() => default!;

    static T? DefQ<T>() => default;

    static string Opt(
        int i = default,
        string? s = default,
        Vec v = default,
        Level l = default,
        long g = default,
        decimal m = default,
        bool b = default,
        double d = default,
        Guid id = default,
        DateTime t = default,
        int? ni = default
    ) =>
        i
        + "|"
        + (s ?? "null")
        + "|"
        + v
        + "|"
        + (int)l
        + "|"
        + g
        + "|"
        + m
        + "|"
        + (b ? "T" : "F")
        + "|"
        + d
        + "|"
        + id
        + "|"
        + t.Year
        + "|"
        + (ni.HasValue ? "has" : "none");

    static string OptT<T>(T x = default!) => x == null ? "null" : x.ToString()!;

    public static string Ints()
    {
        int a = default;
        var b = default(int);
        long c = default;
        short d = default(short);
        byte e = default;
        return a + "|" + b + "|" + c + "|" + d + "|" + e;
    }

    public static string Strings()
    {
        string? a = default;
        var b = default(string);
        return (a == null ? "null" : "x") + (b == null ? "null" : "x");
    }

    public static string Bools()
    {
        bool a = default;
        var d = default(double);
        char c = default;
        return (a ? "T" : "F")
            + "|"
            + d.ToString()
            + "|"
            + ((int)c)
            + "|"
            + (c == '\0' ? "nul" : "other");
    }

    public static string Structs()
    {
        Vec v = default;
        var w = default(Vec);
        RVec r = default;
        return v + "|" + w + "|" + r.X + "," + r.Y.ToString();
    }

    public static string StructIndependent()
    {
        Vec v = default;
        Vec w = default;
        v.X = 5;
        return v.X + "|" + w.X;
    }

    public static string Enums()
    {
        Level l = default;
        var z = default(Zeroed);
        Mode m = default;
        return (int)l
            + "|"
            + z
            + "|"
            + (m == Mode.Fast ? "fast" : "notfast")
            + "|"
            + (l == Level.Low ? "low" : "notlow");
    }

    public static string Bcl()
    {
        Guid g = default;
        DateTime d = default;
        TimeSpan t = default;
        decimal m = default;
        DateOnly day = default;
        return g
            + "|"
            + d.Year
            + "-"
            + d.Ticks
            + "|"
            + t.TotalSeconds.ToString()
            + "|"
            + m
            + "|"
            + day.Year
            + "|"
            + (g == Guid.Empty ? "empty" : "no");
    }

    public static string Nullables()
    {
        int? a = default;
        var b = default(int?);
        Vec? v = default;
        Level? l = default;
        return (a.HasValue ? "has" : "none")
            + (b == null ? "null" : "x")
            + (v == null ? "null" : "x")
            + (l == null ? "null" : "x");
    }

    public static string Generic() =>
        N(Def<int>())
        + "|"
        + N(Def<string>())
        + "|"
        + N(Def<bool>() ? "T" : "F")
        + "|"
        + N(Def<Vec>())
        + "|"
        + N(Def<long>())
        + "|"
        + N(Def<decimal>())
        + "|"
        + N(Def<Level>() == Level.Low ? "low" : "notlow")
        + "|"
        + N(Def<int?>())
        + "|"
        + N(Def<double>())
        + "|"
        + N(Def<Guid>());

    public static string GenericQ() =>
        N(DefQ<int>()) + "|" + N(DefQ<string>()) + "|" + N(DefQ<Vec>());

    public static string Tuples()
    {
        (int, string?) t = default;
        var u = default((int A, bool B));
        return t.Item1 + "|" + (t.Item2 ?? "null") + "|" + u.A + "|" + (u.B ? "T" : "F");
    }

    public static string Optional() => Opt();

    public static string OptionalGeneric() =>
        OptT<int>() + "|" + OptT<string>() + "|" + OptT<Vec>() + "|" + OptT<bool>().ToLower();

    public static string Ternary()
    {
        var c = "x".Length > 0;
        int a = c ? default : 5;
        string? s = c ? default : "x";
        Vec v = c ? default : new Vec { X = 1 };
        return a + "|" + (s ?? "null") + "|" + v;
    }

    public static string Compare()
    {
        int a = 0;
        Vec v = new Vec();
        Guid g = Guid.Empty;
        return (a == default ? "T" : "F")
            + (v.X == default ? "T" : "F")
            + (g == default ? "T" : "F")
            + ("" == default ? "T" : "F");
    }

    public static string Fields()
    {
        var d = new Defaults();
        return (int)d.L
            + "|"
            + d.Z
            + "|"
            + (d.M == Mode.Fast ? "fast" : "other")
            + "|"
            + (d.NL == null ? "null" : "x")
            + "|"
            + d.I
            + "|"
            + (d.S ?? "null")
            + "|"
            + (d.B ? "T" : "F")
            + "|"
            + d.D.ToString()
            + "|"
            + d.Lg
            + "|"
            + d.Dec
            + "|"
            + d.V
            + "|"
            + d.Dt.Year
            + "|"
            + d.G
            + "|"
            + (int)d.C
            + "|"
            + d.Ts.Ticks
            + "|"
            + (int)d.P
            + "|"
            + d.Q
            + "|"
            + (int)Defaults.SL;
    }

    public static string EnumFieldMath()
    {
        var d = new Defaults();
        return ((int)d.L + 1)
            + "|"
            + (d.L == default ? "T" : "F")
            + "|"
            + (d.L == Level.Low ? "T" : "F")
            + "|"
            + d.L.ToString()
            + "|"
            + (d.Z == Zeroed.None ? "T" : "F");
    }

    public static string Arrays()
    {
        var a = new int[2];
        var s = new string?[2];
        var v = new Vec[1];
        var b = new bool[1];
        var l = new Level[1];
        return a[0]
            + "|"
            + (s[0] ?? "null")
            + "|"
            + v[0].X
            + "|"
            + (b[0] ? "T" : "F")
            + "|"
            + (int)l[0];
    }

    public static string Returned()
    {
        return RetVec().X + "|" + (RetStr() ?? "null") + "|" + RetLong() + "|" + RetDec();
    }

    static Vec RetVec() => default;

    static string? RetStr() => default;

    static long RetLong() => default;

    static decimal RetDec() => default;

    public static string OutParams()
    {
        Fill(out var i, out var s, out var v);
        return i + "|" + (s ?? "null") + "|" + v;
    }

    static void Fill(out int i, out string? s, out Vec v)
    {
        i = default;
        s = default;
        v = default;
    }

    public static string StructParam() =>
        TakeVec() + "|" + TakeVec(new Vec { X = 2 }) + "|" + TakeR();

    static string TakeVec(Vec v = default) => v.ToString();

    static string TakeR(RVec r = default) => r.X + ":" + r.Y.ToString();

    public static string IsDefault()
    {
        int i = 0;
        string? s = null;
        return (EqualityComparer<int>.Default.Equals(i, default) ? "T" : "F")
            + (s is default(string) ? "T" : "F")
            + (i is default(int) ? "T" : "F");
    }
}
