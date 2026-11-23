using Metano.Annotations;

namespace Conformance;

public delegate int W6Folder(params int[] xs);

public delegate int W6Listener([This] W6Holder self, int x);

public delegate int W6Three([This] W6Holder self, int x, int y);

public class W6Holder
{
    public int V = 1;
    public Func<W6Holder, int, int> Plain { get; set; } = (h, x) => h.V + x + 100;
    public W6Listener Bound { get; set; } = (self, x) => self.V + x;
    public W6Three Three { get; set; } = (self, x, y) => self.V * 1000 + x * 10 + y;
}

public class W6Bag
{
    public int[] Xs;
    public string Tag;

    public W6Bag(string tag, params int[] xs)
    {
        Tag = tag;
        Xs = xs;
    }

    public W6Bag()
        : this("none") { }

    public W6Bag(int a)
        : this("one", a, a + 1, a + 2) { }

    public W6Bag(int[] arr, bool copy)
        : this("arr", arr) { }

    public string Show() => Tag + ":" + Xs.Length + ":" + string.Join(",", Xs);
}

/// <summary>Wave 6 group G: delegate conversions and invocations, params chains.</summary>
public static class W6DelegateProbes
{
    static int Fold(W6Folder f) => f(1, 2, 3);

    public static string LambdaToParamsDelegate()
    {
        W6Folder count = xs => xs.Length;
        W6Folder sum = xs =>
        {
            var total = 0;
            foreach (var x in xs)
                total += x;
            return total;
        };

        return $"{count(4, 5)} {sum(1, 2, 3)} {Fold(xs => xs.Length * 10)} {count()}";
    }

    public static string DelegateInvokeMethodGroup()
    {
        var h = new W6Holder { V = 2 };
        Func<W6Holder, int, int> plain = h.Plain.Invoke;
        W6Listener rebound = h.Plain.Invoke;
        W6Listener same = h.Bound.Invoke;

        return $"{plain(h, 1)} {rebound(h, 2)} {same(h, 3)}";
    }

    public static string ThisDelegateNamedArguments()
    {
        var h = new W6Holder { V = 5 };
        var a = h.Bound;

        return $"{a.Invoke(h, x: 20)} {a(h, x: 21)} {a.Invoke(x: 22, self: h)}";
    }

    public static string ExpandedParamsInThisChain() =>
        $"{new W6Bag().Show()} {new W6Bag(5).Show()} {new W6Bag([9, 8], true).Show()}";

    public static string ThisDelegateNamedArgumentsOutOfOrder()
    {
        var h = new W6Holder { V = 5 };
        var a = h.Three;

        return $"{a.Invoke(h, y: 1, x: 2)} {a(h, y: 3, x: 4)} {a.Invoke(y: 5, self: h, x: 6)}";
    }

    static string ParamsToPlainGroup()
    {
        try
        {
            W6Folder f = xs => xs.Length;
            Func<int[], int> g = f.Invoke;
            return g([1, 2, 3]).ToString();
        }
        catch (Exception)
        {
            return "threw";
        }
    }

    static string PlainToParamsGroup()
    {
        try
        {
            Func<int[], int> g = xs => xs.Length;
            W6Folder f = g.Invoke;
            return f(1, 2, 3).ToString();
        }
        catch (Exception)
        {
            return "threw";
        }
    }

    public static string InvokeGroupAcrossParamsShapesIsLoud() =>
        $"{ParamsToPlainGroup()} {PlainToParamsGroup()}";
}
