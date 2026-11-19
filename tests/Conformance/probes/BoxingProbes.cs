namespace Conformance;

public enum BxColor
{
    Red,
    Green,
    Blue,
}

[Flags]
public enum BxPerms
{
    None = 0,
    Read = 1,
    Write = 2,
}

/// <summary>Boxing of enums at C# boxing conversions (spec 011 FR-050 / FR-051).</summary>
public static class BoxingProbes
{
    public static string GetTypeName()
    {
        object o = BxColor.Green;

        return o.GetType().Name + "|" + o.GetType().FullName;
    }

    public static string ToStringDeclared()
    {
        object o = BxColor.Green;

        return o.ToString() + "|" + $"{o}";
    }

    public static string ToStringUndeclared()
    {
        object o = (BxColor)9;

        return o.ToString()!;
    }

    public static string ToStringFlags()
    {
        object both = BxPerms.Read | BxPerms.Write;
        object none = BxPerms.None;
        object extra = (BxPerms)9;

        return both + "|" + none + "|" + extra;
    }

    public static string EqualsBoxAndBox()
    {
        object a = BxColor.Red;
        object b = BxColor.Red;
        object c = BxColor.Blue;
        object other = BxPerms.None;

        return a.Equals(b) + "|" + a.Equals(c) + "|" + a.Equals(other);
    }

    public static string EqualsBoxAndRaw()
    {
        object a = BxColor.Red;

        return a.Equals(BxColor.Red) + "|" + a.Equals(0) + "|" + a.Equals(BxColor.Green);
    }

    public static string SwitchOnObject()
    {
        object[] values = [BxColor.Red, BxColor.Blue, BxPerms.Write, 2, "x"];
        var result = "";

        foreach (var value in values)
        {
            result += value switch
            {
                BxColor.Red => "red",
                BxColor c => "color:" + (int)c,
                BxPerms p => "perms:" + (int)p,
                _ => "other",
            };

            result += ";";
        }

        return result;
    }

    public static string SwitchStatementOnObject()
    {
        object o = BxColor.Blue;

        switch (o)
        {
            case BxColor.Red:
                return "red";
            case BxColor.Blue:
                return "blue";
            default:
                return "other";
        }
    }

    public static string CastBack()
    {
        object o = BxColor.Blue;
        var c = (BxColor)o;

        return (int)c + "|" + (c == BxColor.Blue);
    }

    public static string IsTests()
    {
        object member = BxColor.Green;
        object stranger = 7;
        object undeclared = (BxColor)7;

        return (member is BxColor)
            + "|"
            + (member is BxPerms)
            + "|"
            + (stranger is BxColor)
            + "|"
            + (undeclared is BxColor)
            + "|"
            + (member is BxColor.Green)
            + "|"
            + (member is BxColor.Red);
    }

    public static string PatternBinding()
    {
        object o = BxColor.Blue;

        return o is BxColor c ? ((int)c).ToString() : "none";
    }

    public static string Nullable()
    {
        BxColor? missing = null;
        BxColor? present = BxColor.Blue;
        object a = missing;
        object b = present;

        return (a == null) + "|" + b.GetType().Name + "|" + b;
    }

    public static string ListStaysRaw()
    {
        List<BxColor> colors = [BxColor.Red, BxColor.Green];

        return colors.IndexOf(BxColor.Green) + "|" + colors.Contains(BxColor.Red);
    }
}
