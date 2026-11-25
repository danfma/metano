namespace Conformance;

/// <summary>
/// A local declared in one switch section and assigned and read in a later one (#755): the
/// later section runs without the declaring one, so the local must already be in scope.
/// </summary>
public static class SwitchSharedLocalProbes
{
    static int Constant(int k)
    {
        switch (k)
        {
            case 1:
                int x = 1;
                return x;
            case 2:
                x = 2;
                return x * 10;
            default:
                return 0;
        }
    }

    static string Tested(object? o)
    {
        switch (o)
        {
            case null:
                var text = "null";
                return text;
            case string s:
                text = "string:" + s;
                return text;
            default:
                text = "other";
                return text;
        }
    }

    public static string ConstantSwitchLaterSection() =>
        $"{Constant(1)},{Constant(2)},{Constant(3)}";

    public static string TestedSwitchLaterSection() => $"{Tested(null)},{Tested("a")},{Tested(3)}";

    static string OutVar(int k)
    {
        switch (k)
        {
            case 1:
                int.TryParse("5", out var p);
                return "o" + p;
            default:
                p = 9;
                return "o" + p;
        }
    }

    public static string OutVarLaterSection() => $"{OutVar(1)},{OutVar(2)}";

    static bool TryGet(string s, out int value)
    {
        value = s.Length;
        return s.Length > 2;
    }

    static string InCondition(int k, string s)
    {
        switch (k)
        {
            case 1:
                if (int.TryParse(s, out var p))
                    return "ok" + p;

                return "no" + p;
            default:
                p = 9;
                return "d" + p;
        }
    }

    static string InLocal(int k, string s)
    {
        switch (k)
        {
            case 1:
                var ok = TryGet(s, out var q);
                return ok + ":" + q;
            default:
                q = 9;
                return "d" + q;
        }
    }

    public static string OutCallResultLaterSection() =>
        $"{InCondition(1, "7")} {InCondition(1, "x")} {InCondition(2, "7")} {InLocal(1, "abc")} {InLocal(2, "abc")}";
}
