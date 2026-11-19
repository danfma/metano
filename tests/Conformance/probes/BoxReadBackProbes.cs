using Metano.Annotations;

namespace Conformance;

// Spec 011 FR-049 / FR-050: an enum read back from an object-typed collection slot (stored raw).
public enum RbHue
{
    Red,
    Green = 5,
    Blue,
}

public class RbMsg
{
    public string Name = "";
    public Dictionary<string, object> Props = new();
    public List<object> Args = new();
}

public static class RbAProbes
{
    private static string B(bool b) => b ? "T" : "F";

    public static string ListForeach()
    {
        var xs = new List<object> { RbHue.Green, "s", 5, RbHue.Red };
        var n = 0;
        var ints = 0;

        foreach (var o in xs)
        {
            if (o is RbHue)
                n++;

            if (o is int)
                ints++;
        }

        return n + "|" + ints;
    }

    public static string ListIndex()
    {
        var xs = new List<object> { RbHue.Green, "s" };
        object first = xs[0];

        return B(xs[0] is RbHue)
            + B(first is RbHue)
            + B(xs[0] is RbHue h && h == RbHue.Green)
            + B(xs[0] is RbHue.Green)
            + B(xs[1] is RbHue)
            + B(xs[0] is int);
    }

    public static string ListAdd()
    {
        var xs = new List<object>();
        xs.Add(RbHue.Blue);
        object o = RbHue.Red;
        xs.Add(o);
        return B(xs[0] is RbHue) + B(xs[1] is RbHue) + B(xs[1] is RbHue.Red);
    }

    public static string ListLinq()
    {
        var xs = new List<object> { RbHue.Green, "s", RbHue.Red };

        return xs.Count(x => x is RbHue)
            + "|"
            + xs.Where(x => x is RbHue).Count()
            + "|"
            + B(xs.Any(x => x is RbHue.Red))
            + "|"
            + B(xs.First() is RbHue);
    }

    public static string ListSwitch()
    {
        var xs = new List<object> { RbHue.Green, 5, "s" };
        var r = "";

        foreach (var o in xs)
        {
            switch (o)
            {
                case RbHue h:
                    r += "hue" + (int)h;
                    break;
                case int i:
                    r += "int" + i;
                    break;
                default:
                    r += "other";
                    break;
            }

            r += ",";
        }

        return r;
    }

    public static string ListCast()
    {
        var xs = new List<object> { RbHue.Green };
        var h = (RbHue)xs[0];
        var a = xs[0] as RbHue?;
        return (int)h + "|" + B(a.HasValue) + "|" + B(h == RbHue.Green);
    }
}

public static class RbBProbes
{
    private static string B(bool b) => b ? "T" : "F";

    public static string DictValue()
    {
        var d = new Dictionary<string, object> { ["a"] = RbHue.Green };
        d["b"] = RbHue.Red;

        return B(d["a"] is RbHue)
            + B(d["b"] is RbHue)
            + B(d.TryGetValue("a", out var v) && v is RbHue)
            + B(d.Values.All(x => x is RbHue));
    }

    public static string ArrayElem()
    {
        var arr = new object[] { RbHue.Green, 1 };
        var arr2 = new object[2];
        arr2[0] = RbHue.Blue;

        return B(arr[0] is RbHue)
            + B(arr[1] is RbHue)
            + B(arr2[0] is RbHue)
            + B(arr[0] is RbHue.Green);
    }

    public static string MemberBag()
    {
        var m = new RbMsg();
        m.Props["hue"] = RbHue.Blue;
        m.Args.Add(RbHue.Red);

        return B(m.Props["hue"] is RbHue)
            + B(m.Args[0] is RbHue)
            + B(m.Args[0] is RbHue h && h == RbHue.Red);
    }

    public static string QueueStack()
    {
        var q = new Queue<object>();
        q.Enqueue(RbHue.Green);
        var s = new Stack<object>();
        s.Push(RbHue.Blue);
        return B(q.Peek() is RbHue) + B(q.Dequeue() is RbHue) + B(s.Pop() is RbHue);
    }

    public static string ParamsArray() => Count(RbHue.Green, 1, "s", RbHue.Red).ToString();

    private static int Count(params object[] xs)
    {
        var n = 0;

        foreach (var x in xs)
            if (x is RbHue)
                n++;

        return n;
    }

    public static string ToArrayAndBack()
    {
        var xs = new List<object> { RbHue.Green };
        var arr = xs.ToArray();
        var copy = new List<object>(xs);
        var sel = xs.Select(x => x).ToList();
        return B(arr[0] is RbHue) + B(copy[0] is RbHue) + B(sel[0] is RbHue);
    }

    public static string TypedToObject()
    {
        var hs = new List<RbHue> { RbHue.Green };
        var os = hs.Select(h => (object)h).ToList();
        IEnumerable<object> e = hs.Cast<object>();
        var n = 0;

        foreach (object o in hs)
            if (o is RbHue)
                n++;

        return B(os[0] is RbHue) + n;
    }
}

// Spec 011 FR-049 / FR-050: composite patterns over a boxed enum.
public enum BpHue
{
    Red,
    Green = 5,
    Blue,
}

[Flags]
public enum BpAcc
{
    None = 0,
    Read = 1,
    Write = 2,
    Exec = 4,
    All = 7,
}

public record BpWrap(object Item);

public record BpPair(object A, object B);

public class BpNode
{
    public object? Val;
    public BpHue Typed;
    public BpHue? Opt;
}

public static class BoxPAProbes
{
    private static string B(bool b) => b ? "T" : "F";

    private static string P1(object o) =>
        B(o is BpHue and not BpHue.Red)
        + B(o is BpHue and (BpHue.Red or BpHue.Green))
        + B(o is BpHue and > BpHue.Red)
        + B(o is BpHue and >= BpHue.Green and < BpHue.Blue)
        + B(o is not BpHue.Red)
        + B(o is BpHue.Red or BpHue.Blue)
        + B(o is BpHue { })
        + B(o is not (BpHue.Red or BpHue.Green));

    public static string AndNot() =>
        P1(BpHue.Red)
        + "|"
        + P1(BpHue.Green)
        + "|"
        + P1(BpHue.Blue)
        + "|"
        + P1(5)
        + "|"
        + P1(0)
        + "|"
        + P1("s");

    private static string P2(object o) =>
        o switch
        {
            BpHue and not BpHue.Red => "nonred",
            BpHue => "red",
            BpAcc and (BpAcc.Read or BpAcc.Write) => "rw",
            BpAcc a when a != BpAcc.None => "acc",
            0 => "zero",
            _ => "?",
        };

    public static string SwitchCombos() =>
        P2(BpHue.Red)
        + ","
        + P2(BpHue.Green)
        + ","
        + P2(BpAcc.Read)
        + ","
        + P2(BpAcc.All)
        + ","
        + P2(BpAcc.None)
        + ","
        + P2(0)
        + ","
        + P2(5);

    private static string P3(object o)
    {
        switch (o)
        {
            case BpHue.Red:
                return "red";
            case BpHue h when h != BpHue.Green:
                return "notgreen";
            case BpHue:
                return "green";
            case BpAcc.Read or BpAcc.Write:
                return "rw";
            case > 3:
                return "gt3";
            case int:
                return "int";
            default:
                return "?";
        }
    }

    public static string SwitchStmtCombos() =>
        P3(BpHue.Red)
        + ","
        + P3(BpHue.Green)
        + ","
        + P3(BpHue.Blue)
        + ","
        + P3(BpAcc.Read)
        + ","
        + P3(BpAcc.All)
        + ","
        + P3(7)
        + ","
        + P3(1)
        + ","
        + P3("s");

    // The same patterns over boxes only (and a box of another enum), free of the accepted
    // raw-number divergence: these rows must match .NET.
    public static string AndNotBoxed() =>
        P1(BpHue.Red)
        + "|"
        + P1(BpHue.Green)
        + "|"
        + P1(BpHue.Blue)
        + "|"
        + P1(BpAcc.None)
        + "|"
        + P1("s");

    public static string SwitchCombosBoxed() =>
        P2(BpHue.Red)
        + ","
        + P2(BpHue.Green)
        + ","
        + P2(BpAcc.Read)
        + ","
        + P2(BpAcc.All)
        + ","
        + P2(BpAcc.None)
        + ","
        + P2("s");

    public static string SwitchStmtCombosBoxed() =>
        P3(BpHue.Red)
        + ","
        + P3(BpHue.Green)
        + ","
        + P3(BpHue.Blue)
        + ","
        + P3(BpAcc.Read)
        + ","
        + P3(BpAcc.All)
        + ","
        + P3("s");
}

public static class BoxPBProbes
{
    private static string B(bool b) => b ? "T" : "F";

    private static string W(BpWrap w) =>
        B(w is { Item: BpHue.Green })
        + B(w is { Item: BpHue })
        + B(w is { Item: BpHue h } && h == BpHue.Green)
        + B(w is { Item: not BpHue.Red })
        + B(w is { Item: BpHue and not BpHue.Red })
        + B(w is { Item: int })
        + B(w is { Item: 5 })
        + B(w is { Item: BpHue.Red or BpHue.Green });

    public static string PropertyPatterns() =>
        W(new BpWrap(BpHue.Green))
        + "|"
        + W(new BpWrap(BpHue.Red))
        + "|"
        + W(new BpWrap(5))
        + "|"
        + W(new BpWrap("s"));

    private static string D(BpPair p) =>
        p switch
        {
            (BpHue.Red, BpHue.Red) => "rr",
            (BpHue a, BpHue b) when a == b => "same",
            (BpHue, int i) => "hi" + i,
            (BpAcc.Read, _) => "read",
            (_, BpHue.Blue) => "xb",
            _ => "?",
        };

    public static string Positional() =>
        D(new BpPair(BpHue.Red, BpHue.Red))
        + ","
        + D(new BpPair(BpHue.Green, BpHue.Green))
        + ","
        + D(new BpPair(BpHue.Green, 5))
        + ","
        + D(new BpPair(BpAcc.Read, "s"))
        + ","
        + D(new BpPair(1, BpHue.Blue))
        + ","
        + D(new BpPair(1, 1));

    private static string N(BpNode n) =>
        B(n is { Val: BpHue.Green, Typed: BpHue.Green })
        + B(n is { Typed: BpHue.Green or BpHue.Blue })
        + B(n is { Typed: not BpHue.Red })
        + B(n is { Opt: BpHue.Red })
        + B(n is { Opt: null })
        + B(n is { Opt: not null and not BpHue.Red })
        + B(n.Val is null)
        + B(n.Val is BpHue.Red);

    public static string TypedMembers() =>
        N(
            new BpNode
            {
                Val = BpHue.Green,
                Typed = BpHue.Green,
                Opt = BpHue.Red,
            }
        )
        + "|"
        + N(new BpNode { Val = BpHue.Red, Typed = BpHue.Red })
        + "|"
        + N(new BpNode { Typed = BpHue.Blue, Opt = BpHue.Green });

    public static string PropertyPatternsBoxed() =>
        W(new BpWrap(BpHue.Green))
        + "|"
        + W(new BpWrap(BpHue.Red))
        + "|"
        + W(new BpWrap(BpAcc.Write))
        + "|"
        + W(new BpWrap("s"));

    public static string PositionalBoxed() =>
        D(new BpPair(BpHue.Red, BpHue.Red))
        + ","
        + D(new BpPair(BpHue.Green, BpHue.Green))
        + ","
        + D(new BpPair(BpHue.Green, BpHue.Blue))
        + ","
        + D(new BpPair(BpAcc.Read, "s"))
        + ","
        + D(new BpPair("s", BpHue.Blue))
        + ","
        + D(new BpPair(BpAcc.None, BpAcc.None));
}

public static class BoxPCProbes
{
    private static string B(bool b) => b ? "T" : "F";

    private static string T(BpHue h) =>
        h switch
        {
            BpHue.Red => "r",
            > BpHue.Green => "big",
            not BpHue.Green => "odd",
            _ => "g",
        };

    public static string TypedPatterns() =>
        T(BpHue.Red)
        + T(BpHue.Green)
        + T(BpHue.Blue)
        + T((BpHue)3)
        + "|"
        + B(BpHue.Red is BpHue.Red)
        + B(BpHue.Green is not BpHue.Red and not BpHue.Blue);

    private static string Nn(BpHue? h) =>
        h switch
        {
            null => "n",
            BpHue.Red => "r",
            BpHue x when x > BpHue.Green => "big",
            _ => "o",
        };

    public static string NullablePatterns() =>
        Nn(null) + Nn(BpHue.Red) + Nn(BpHue.Blue) + Nn(BpHue.Green);

    private static string En(Enum e) =>
        e switch
        {
            BpHue.Red => "r",
            BpHue h => "h" + (int)h,
            BpAcc.Read => "read",
            _ => "?",
        };

    public static string EnumBasePatterns() =>
        En(BpHue.Red) + En(BpHue.Blue) + En(BpAcc.Read) + En(BpAcc.All);

    private static string Eq(object o) =>
        B(o.Equals(BpHue.Red))
        + B(Equals(o, BpHue.Red))
        + B(o is BpHue h && h == BpHue.Red)
        + B(o is BpHue h2 && h2.Equals(BpHue.Red))
        + B((o as BpHue?) == BpHue.Red)
        + B(o is BpHue && (BpHue)o == BpHue.Red);

    public static string EqualityForms() =>
        Eq(BpHue.Red) + "|" + Eq(BpHue.Green) + "|" + Eq(0) + "|" + Eq("s");

    private static string Gen<T>(T v) =>
        B(v is BpHue)
        + B(v is BpHue.Red)
        + B(v is int)
        + B(v is BpHue and not BpHue.Red)
        + (v is BpHue h ? "h" + (int)h : "-");

    public static string GenericPatterns() =>
        Gen(BpHue.Red)
        + "|"
        + Gen(BpHue.Green)
        + "|"
        + Gen<object>(BpHue.Red)
        + "|"
        + Gen(0)
        + "|"
        + Gen("s");

    public static string EqualityFormsBoxed() =>
        Eq(BpHue.Red) + "|" + Eq(BpHue.Green) + "|" + Eq(BpAcc.None) + "|" + Eq("s");

    public static string GenericPatternsBoxed() =>
        Gen<object>(BpHue.Red)
        + "|"
        + Gen<object>(BpHue.Green)
        + "|"
        + Gen<object>(BpAcc.None)
        + "|"
        + Gen<object>("s");
}
