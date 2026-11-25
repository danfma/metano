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
}
