namespace Conformance;

/// <summary>Numeric tuple, decimal and cast rows of the wave-6 group J fixes.</summary>
public static class NumericTupleProbes
{
    static int I(int x) => x;

    public static string UncheckedConstantElements()
    {
        var a = unchecked(((long, long))(18446744073709551615UL, I(1)));
        var b = unchecked(((ulong, long))(-1, I(2)));
        var c = unchecked(((int, long))(3000000000L, I(3)));
        var d = ((long, long))(2.7, I(4));

        return a.Item1
            + ","
            + a.Item2
            + "|"
            + b.Item1
            + ","
            + b.Item2
            + "|"
            + c.Item1
            + ","
            + c.Item2
            + "|"
            + d.Item1
            + ","
            + d.Item2;
    }
}
