namespace Conformance;

// Spec 011 FR-049 / FR-050: `<=` after an `as E` cast (the cast is parenthesized).
public static class BpLeProbes
{
    public static string CastLe()
    {
        int n = 5;

        return "" + ((BpHue)n <= BpHue.Blue) + (BpHue.Green + 1 <= BpHue.Blue);
    }

    public static string PatLe()
    {
        object o = BpHue.Green;

        return ""
            + (o is BpHue and <= BpHue.Blue)
            + (o is BpHue and (<= BpHue.Green or > BpHue.Blue));
    }

    public static string SwLe()
    {
        object o = BpHue.Blue;

        return o switch
        {
            BpHue and <= BpHue.Green => "le",
            BpHue => "h",
            _ => "?",
        };
    }
}
