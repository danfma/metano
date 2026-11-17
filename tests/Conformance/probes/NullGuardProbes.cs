namespace Conformance;

public class Holder
{
    public string? MaybeName;
    public string Name = "n";
    public string? Prop { get; set; }
    public string Solid { get; set; } = "s";

    public void Clear()
    {
        MaybeName = null;
        Prop = null;
    }

    public override string ToString() => "Holder!";
}

public class Plain
{
    public int V = 3;
}

public static class NullGuardProbes
{
    static string? Maybe(bool n) => n ? null : "v";

    static string NotNull() => "nn";

    static T Id<T>(T x) => x;

    static string? _field;

    public static string ConcatNullableLocal()
    {
        string? s = Maybe(true);
        return "a" + s + "b";
    }

    public static string ConcatBang()
    {
        string? s = Maybe(true);
        return "a" + s! + "b";
    }

    public static string ConcatBangValue()
    {
        string? s = Maybe(false);
        return "a" + s! + "b";
    }

    public static string InterpBang()
    {
        string? s = Maybe(true);
        return $"a{s!}b";
    }

    public static string InterpNullable()
    {
        string? s = Maybe(true);
        return $"a{s}b";
    }

    public static string InterpAligned()
    {
        string? s = Maybe(true);
        return $"a{s, 3}b{s!, -2}c";
    }

    public static string CompoundPlusEq()
    {
        var r = "a";
        string? s = Maybe(true);
        r += s;
        r += s!;
        r += Maybe(false);
        return r + "|";
    }

    public static string CompoundOnNull()
    {
        string? r = null;
        r += "x";
        string? q = null;
        q += null;
        return r + "|" + (q == null ? "null" : "[" + q + "]");
    }

    public static string ConcatIntNullable()
    {
        int? i = null;
        int? j = 5;
        return "a" + i + "b" + j;
    }

    public static string ConcatObject()
    {
        object? o = null;
        object p = 12;
        return "a" + o + "b" + p;
    }

    public static string ConcatNullLiteral()
    {
        return "a" + null + "b";
    }

    public static string ConcatTwoNulls()
    {
        string? a = null;
        string? b = null;
        var r = a + b;
        return r == null ? "null" : "[" + r + "]";
    }

    public static string ConcatField()
    {
        _field = null;
        return "f" + _field + "g";
    }

    public static string ConcatAfterCheckThenClear()
    {
        var h = new Holder { MaybeName = "x", Prop = "y" };
        if (h.MaybeName != null && h.Prop != null)
        {
            h.Clear();
            return "a" + h.MaybeName + "b" + h.Prop + "c";
        }
        return "no";
    }

    public static string ConcatLambdaClears()
    {
        string? s = "x";
        Action clear = () => s = null;
        if (s != null)
        {
            clear();
            return "a" + s + "b";
        }
        return "no";
    }

    public static string ConcatDeclaredNonNull()
    {
        var h = new Holder();
        return h.Name + h.Solid + NotNull() + "k";
    }

    public static string ConcatClassInstance()
    {
        var h = new Holder();
        Holder? n = null;
        return "a" + h + "b" + n + "c";
    }

    public static string ConcatGeneric()
    {
        return Gen<string?>(null) + Gen(5) + Gen("s");
    }

    static string Gen<T>(T x) => "g" + x + "h";

    public static string InterpGeneric()
    {
        return GenI<string?>(null) + GenI(5) + GenI(true) + GenI(1.5);
    }

    static string GenI<T>(T x) => $"g{x}h";

    public static string ConcatTernary()
    {
        string? s = Maybe(true);
        return "a" + (s == null ? Maybe(true) : "z") + "b";
    }

    public static string ConcatCoalesce()
    {
        string? s = Maybe(true);
        return "a" + (s ?? Maybe(true)) + "b" + (s ?? "d");
    }

    public static string ConcatCondAccess()
    {
        Holder? h = null;
        return "a" + h?.Name + "b" + h?.Name.Length + "c";
    }

    public static string ConcatArrayElement()
    {
        var xs = new string?[] { null, "x" };
        var ys = new[] { "p" };
        return "a" + xs[0] + xs[1] + ys[0];
    }

    public static string ConcatDict()
    {
        var d = new Dictionary<string, string?> { ["k"] = null };
        return "a" + d["k"] + "b";
    }

    public static string ConcatChar()
    {
        char c = 'x';
        char? n = null;
        return "a" + c + n + 'y';
    }

    public static string ConcatMethodResult()
    {
        return "a" + Maybe(true) + Id<string?>(null) + Id("q");
    }

    public static string ConcatToString()
    {
        var p = new Plain();
        object o = 5;
        return "a" + o.ToString() + p.V.ToString();
    }

    public static string ConcatEnum()
    {
        Color? d = null;
        return "a" + d + "b";
    }

    public static string ConcatLong()
    {
        long? l = null;
        long m = 9007199254740993L;
        return "a" + l + m;
    }

    public static string ConcatDecimal()
    {
        decimal? l = null;
        decimal m = 1.50m;
        return "a" + l + m;
    }

    public static string ExMessageNull()
    {
        string? m = Maybe(true);
        try
        {
            throw new InvalidOperationException(m);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageNullBase()
    {
        string? m = Maybe(true);
        try
        {
            throw new Exception(m);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageNullArg()
    {
        string? m = Maybe(true);
        try
        {
            throw new ArgumentException(m);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageValue()
    {
        string? m = Maybe(false);
        try
        {
            throw new InvalidOperationException(m);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageBang()
    {
        string? m = Maybe(true);
        try
        {
            throw new InvalidOperationException(m!);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageConcat()
    {
        string? m = Maybe(true);
        try
        {
            throw new InvalidOperationException("p:" + m);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageCustom()
    {
        string? m = Maybe(true);
        try
        {
            throw new MyError(m);
        }
        catch (MyError e)
        {
            return e.Message + "|" + e.Code;
        }
    }

    public static string ExNoArg()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ListPatternNulls()
    {
        var xs = new string?[] { null, "b" };
        var r = "";
        if (xs is [null, var b])
            r += "1" + b;
        if (xs is [var a, "b"])
            r += "2" + a;
        if (xs is [.., { Length: 1 } last])
            r += "3" + last;
        if (xs is [{ } first, ..])
            r += "4" + first;
        return r;
    }

    public static string ListPatternNullList()
    {
        int[]? xs = null;
        List<string>? ys = null;
        return (xs is [1, ..]) + "|" + (ys is []) + "|" + (xs is not [..]);
    }

    public static string StringConcatCalls()
    {
        string? s = Maybe(true);
        return string.Join(",", "x", s, "y") + string.Join("-", new string?[] { null, "q", null });
    }

    public static string NullableBoolDouble()
    {
        bool? b = null;
        double? d = null;
        float? f = null;
        return "a" + b + d + f + "z" + $"{b}{d}{f}";
    }
}

public class MyError : Exception
{
    public int Code { get; }

    public MyError(string? message)
        : base(message)
    {
        Code = 7;
    }
}
