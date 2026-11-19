namespace Conformance;

// Spec 011 FR-050: a designator after a constant or an `or` of constants binds the unboxed enum.
public static class BpDesProbes
{
    public static string ConstThenType()
    {
        object o = BpHue.Blue;
        var r = "";

        if (o is BpHue.Blue and BpHue h)
            r += ((int)h + 1) + "," + (h == BpHue.Blue) + "," + (h > BpHue.Green);

        return r;
    }

    public static string TypeThenConst()
    {
        object o = BpHue.Blue;
        var r = "";

        if (o is BpHue h and BpHue.Blue)
            r += ((int)h + 1) + "," + (h == BpHue.Blue);

        return r;
    }

    public static string OrThenType()
    {
        object o = BpHue.Green;

        return o switch
        {
            (BpHue.Red or BpHue.Green) and BpHue h => "" + ((int)h + 1) + (h == BpHue.Green),
            _ => "?",
        };
    }

    public static string RelThenType()
    {
        object o = BpHue.Blue;

        return o is > BpHue.Green and BpHue h ? "" + ((int)h * 2) : "?";
    }

    public static string NotNullThenType()
    {
        object o = BpHue.Green;

        return o is not null and BpHue h ? "" + ((int)h + 1) : "?";
    }

    public static string PropConstType()
    {
        var w = new BpWrap(BpHue.Blue);

        return w is { Item: BpHue.Blue and BpHue h } ? "" + ((int)h + 1) + (h == BpHue.Blue) : "?";
    }

    public static string CaseConstType()
    {
        object o = BpHue.Blue;

        switch (o)
        {
            case BpHue.Blue and BpHue h:
                return "" + ((int)h + 1);
            default:
                return "?";
        }
    }
}
