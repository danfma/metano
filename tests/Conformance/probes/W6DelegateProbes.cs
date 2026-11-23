using Metano.Annotations;

namespace Conformance;

public delegate int W6Folder(params int[] xs);

public delegate int W6Listener([This] W6Holder self, int x);

public class W6Holder
{
    public int V = 1;
    public Func<W6Holder, int, int> Plain { get; set; } = (h, x) => h.V + x + 100;
    public W6Listener Bound { get; set; } = (self, x) => self.V + x;
}

/// <summary>Wave 6 group G: delegate conversions and invocations.</summary>
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
}
