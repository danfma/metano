namespace Conformance;

[Flags]
public enum RgPerms
{
    None = 0,
    Read = 1,
    Write = 2,
}

public enum RgColor
{
    Red,
    Green,
}

public static class RangeProbes
{
    public static string UIntCastOk()
    {
        object o = 3000000000u;

        return ((uint)o).ToString();
    }

    public static string UShortCastOk()
    {
        object o = (ushort)40000;

        return ((ushort)o).ToString();
    }

    public static string SByteCastOk()
    {
        object o = (sbyte)-1;

        return ((sbyte)o).ToString();
    }

    public static string UIntCastFail()
    {
        try
        {
            object o = -1;

            return ((uint)o).ToString();
        }
        catch (InvalidCastException e)
        {
            return e.Message;
        }
    }

    public static string SByteCastFail()
    {
        try
        {
            object o = 200;

            return ((sbyte)o).ToString();
        }
        catch (InvalidCastException e)
        {
            return e.Message;
        }
    }

    public static string FoldedIntegralTests()
    {
        object big = 3000000000u;
        object wide = (ushort)40000;
        object small = (sbyte)-1;
        object negative = -1;

        return (big is uint)
            + "|"
            + (wide is ushort)
            + "|"
            + (small is sbyte)
            + "|"
            + (negative is uint);
    }

    public static string EnumUndeclaredRoundTrip()
    {
        var color = (RgColor)7;
        object boxed = color;

        return ((int)(RgColor)boxed).ToString();
    }

    public static string EnumUndeclaredIs()
    {
        object boxed = (RgColor)7;

        return (boxed is RgColor).ToString();
    }

    public static string FlagsUndeclaredBit()
    {
        object boxed = (RgPerms)(1 | 8);

        return ((RgPerms)boxed == (RgPerms)9)
            + "|"
            + (boxed is RgPerms)
            + "|"
            + (int)(RgPerms)boxed;
    }

    public static string EnumSwitchPattern()
    {
        object boxed = RgColor.Green;

        return boxed switch
        {
            RgColor color => "color " + (int)color,
            string text => text,
            _ => "other",
        };
    }

    public static string EnumDynamicDispatch()
    {
        dynamic value = RgColor.Green;

        return Describe(value);
    }

    public static string Describe(RgColor color) => "color " + (int)color;

    public static string Describe(string text) => "text " + text;
}
