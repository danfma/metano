namespace Conformance;

public enum Shade
{
    Light,
    Medium,
    Dark,
}

[System.Flags]
public enum Access
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}

public enum Wide : long
{
    Small = 1,
    Large = 1099511627776,
}

/// <summary>
/// Numeric enums as <c>as const</c> objects (spec 011 FR-044): member values,
/// casts, operators, increments, switches and <c>[Flags]</c> combinations.
/// </summary>
public static class EnumConstProbes
{
    static Shade FromNumber(int n) => (Shade)n;

    static string Label(Shade s) =>
        s switch
        {
            Shade.Light => "light",
            Shade.Medium => "medium",
            Shade.Dark => "dark",
            _ => "other",
        };

    public static string MemberValues() =>
        (int)Shade.Light + "," + (int)Shade.Medium + "," + (int)Shade.Dark + "," + (long)Wide.Large;

    public static string CastsAndSwitch() =>
        Label(FromNumber(2))
        + "|"
        + Label((Shade)0)
        + "|"
        + Label(FromNumber(7))
        + "|"
        + (int)FromNumber(7);

    public static string Operators()
    {
        var s = Shade.Light;
        var next = s + 1;
        var combined = s | Shade.Dark;
        var inverted = ~Shade.Medium;
        var distance = Shade.Dark - Shade.Light;

        return (int)next + "," + (int)combined + "," + (int)inverted + "," + distance;
    }

    public static string Increments()
    {
        var s = Shade.Light;
        s++;
        var afterIncrement = (int)s;
        s += 1;
        var afterCompound = (int)s;
        s--;

        return afterIncrement + "," + afterCompound + "," + (int)s + "," + (s == Shade.Medium);
    }

    public static string Defaults()
    {
        Shade s = default;
        Access a = default;

        return (int)s + "," + (int)a + "," + (s == Shade.Light) + "," + (a == Access.None);
    }

    public static string Flags()
    {
        var p = Access.Read | Access.Write;
        p |= Access.Execute;
        p &= ~Access.Read;
        var toggled = p ^ Access.Write;
        var canWrite = (p & Access.Write) != 0;

        return (int)p
            + ","
            + (int)toggled
            + ","
            + canWrite
            + ","
            + p.HasFlag(Access.Execute)
            + ","
            + (int)(Access)3;
    }

    public static string NullableCast()
    {
        int? present = 1;
        int? missing = null;
        var a = (Shade?)present;
        var b = (Shade?)missing;

        return (a == Shade.Medium) + "," + (b == null);
    }
}
