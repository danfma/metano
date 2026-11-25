namespace Conformance;

public record DeconPoint(int X, int Y);

public class DeconPair
{
    public void Deconstruct(out int first, out string second)
    {
        first = 7;
        second = "q";
    }
}

public class DeconHolder
{
    public int Field;
    public int Prop { get; set; }
    private int _a = 1;
    private int _b = 2;

    public void Swap() => (_a, _b) = (_b, _a);

    public string State => _a + "," + _b;
}

public class DeconLog
{
    public void Deconstruct(out int a, out int b)
    {
        DeconstructionProbes.Trace += "d";
        a = 1;
        b = 2;
    }
}

public static class DeconRunner
{
    public static (int, Action) Run(Func<Action, (int, Action)> body) => body(() => { });
}

// #779: every deconstruction C# accepts in statement position.
public static class DeconstructionProbes
{
    private static string _log = "";

    public static string Trace = "";

    private static int Log(int value)
    {
        _log += value;

        return value;
    }

    private static int[] Cells(string tag)
    {
        _log += tag;

        return new int[3];
    }

    private static (int, string) Pair()
    {
        _log += "P";

        return (5, "pp");
    }

    // The issue's repro: .NET prints 44.
    public static string IssueRepro()
    {
        var (n, dispose) = DeconRunner.Run(d => (42, d));
        dispose();
        var (m, s) = (1, "x");

        return (n + m + s.Length).ToString();
    }

    public static string RecordAndMethod()
    {
        var (x, y) = new DeconPoint(1, 2);
        var (a, b) = new DeconPair();

        return x + "," + y + "," + a + b;
    }

    public static string SwapAndMixed()
    {
        int x = 1,
            y = 2;
        (x, y) = (y, x);
        (x, var z) = Pair();

        return x + "," + y + "," + z;
    }

    public static string NestedAndDiscards()
    {
        _log = "";
        var t = (1, (new DeconPoint(3, 4), new DeconPair()));
        var (a, ((x, y), (p, q))) = t;
        var (_, (b, _)) = (Log(9), (2, Log(8)));

        return a + "," + x + y + "," + p + q + "," + b + "," + _log;
    }

    public static string EvaluationOrder()
    {
        _log = "";
        var holder = new DeconHolder();
        var cells = new int[3];
        (Cells("A")[Log(1)], cells[Log(2)], holder.Field) = (Log(3), Log(4), Log(5));

        return _log + "," + cells[2] + "," + holder.Field;
    }

    public static string Loops()
    {
        var dictionary = new Dictionary<string, int> { ["a"] = 1, ["bb"] = 2 };
        var points = new List<DeconPoint> { new(1, 2), new(3, 4) };
        var tuples = new[] { (1, "a"), (2, "bb") };
        var total = 0;

        foreach (var (k, v) in dictionary)
            total += k.Length * v;

        foreach (var (x, y) in points)
            total += x * y;

        foreach ((var n, var s) in tuples)
            total += n + s.Length;

        return total.ToString();
    }

    public static string LambdaAndMember()
    {
        var x = 0;
        Action set = () => (x, _) = (9, 1);
        set();
        var holder = new DeconHolder();
        holder.Swap();

        return x + "|" + holder.State;
    }

    // A local written by the deconstruction is read as receiver or index
    // before the right side (#779 review).
    public static string IndexAlias()
    {
        int i = 0;
        var a = new int[] { 0, 0, 0 };
        (i, a[i]) = (1, 2);
        int j = 0;
        var b = new int[] { 0, 0, 0 };
        var k = 5;
        (j, b[j]) = (k, k);

        return i + ":" + string.Join(",", a) + "|" + j + ":" + string.Join(",", b);
    }

    public static string ReceiverAlias()
    {
        var o1 = new DeconHolder();
        var o2 = new DeconHolder();
        var o = o1;
        (o, o.Field) = (o2, 5);

        return o1.Field + "," + o2.Field;
    }

    public static string ClosureIndex()
    {
        int i = 0;
        var a = new int[] { 0, 0, 0 };
        Func<int> bump = () =>
        {
            i++;
            return 7;
        };
        (a[i], i) = (bump(), 2);

        return i + ":" + string.Join(",", a);
    }

    // Every literal element is evaluated before a nested Deconstruct runs.
    public static string NestedDeconstructOrder()
    {
        Trace = "";
        var o = new DeconLog();
        int x = 0,
            y = 0,
            w = 0;
        (x, (y, _), w) = (Mark(1, "f"), o, Mark(3, "g"));

        return Trace + x + y + w;
    }

    private static int Mark(int value, string tag)
    {
        Trace += tag;

        return value;
    }

    private static (int, int) IntPair() => (1, 2);

    // Element conversions of a non-literal right side (int to long / decimal).
    public static string ElementConversions()
    {
        (long l, long m) = IntPair();
        (decimal d, decimal e) = IntPair();
        decimal f = 0,
            g = 0;
        (f, g) = IntPair();

        return (l * 3000000000L + m) + "|" + (d + 0.5m) + "," + e + "|" + (f + g + 0.25m);
    }
}
