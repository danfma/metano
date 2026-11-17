#nullable disable
namespace Conformance;

public class OHolder
{
    public string Name;
    public string Prop { get; set; }
    public OHolder Next;

    public override string ToString() => "OH";
}

public static class ObliviousProbes
{
    static string Maybe(bool n) => n ? null : "v";

    static string _field;

    public static string ConcatLocal()
    {
        string s = Maybe(true);
        return "a" + s + "b";
    }

    public static string ConcatValue()
    {
        string s = Maybe(false);
        return "a" + s + "b";
    }

    public static string Interp()
    {
        string s = Maybe(true);
        return $"a{s}b{s, 2}c";
    }

    public static string Compound()
    {
        var r = "a";
        string s = Maybe(true);
        r += s;
        r += Maybe(false);
        return r + "|";
    }

    public static string CompoundOnNull()
    {
        string r = null;
        r += "x";
        return r;
    }

    public static string Field()
    {
        _field = null;
        var h = new OHolder();
        return "a" + _field + h.Name + h.Prop + "b";
    }

    public static string Instance()
    {
        OHolder h = null;
        var g = new OHolder();
        return "a" + h + g + "b";
    }

    public static string ObjectOp()
    {
        object o = null;
        object p = 4;
        return "a" + o + p;
    }

    public static string ExMessage()
    {
        string m = Maybe(true);
        try
        {
            throw new InvalidOperationException(m);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageValue()
    {
        string m = Maybe(false);
        try
        {
            throw new InvalidOperationException(m);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string ExMessageLiteral()
    {
        try
        {
            throw new InvalidOperationException("lit");
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public static string Generic()
    {
        return Gen<string>(null) + Gen(3);
    }

    static string Gen<T>(T x) => "g" + x + $"{x}h";

    public static string Literals()
    {
        const string K = "k";
        return "a" + K + "b" + 1 + 'c' + 2.5 + true;
    }

    public static string MethodResult()
    {
        return "a" + Maybe(true) + "x".ToUpper() + "y".Substring(0);
    }

    public static string ListPattern()
    {
        var xs = new string[] { null, "b" };
        return (xs is [null, "b"]) + "|" + (xs is [var a, ..] && a == null);
    }

    public static string ArrayElem()
    {
        var xs = new string[] { null };
        return "a" + xs[0] + "b";
    }

    public static string NullCheckThenUse()
    {
        string s = Maybe(false);
        if (s != null)
            return "a" + s;
        return "no";
    }
}
