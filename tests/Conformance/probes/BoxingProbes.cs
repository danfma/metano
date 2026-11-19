using Metano.Annotations;

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

[Flags]
public enum BxWide : long
{
    None = 0,
    Low = 1,
    High = 1L << 40,
}

[Flags]
public enum BxHuge : ulong
{
    None = 0,
    Low = 1,
    Top = 1UL << 63,
}

/// <summary>
/// The console lines a write prints: .NET redirects <c>Console.Out</c>; the
/// TypeScript body swaps <c>console.log</c>, printing a non-string argument by
/// its object tag so a box that reaches <c>console.log</c> unformatted shows.
/// </summary>
public static class BxConsole
{
    [Emit(
        "((write) => { const lines: string[] = []; const log = console.log; console.log = (...args: unknown[]) => lines.push(args.map((a) => typeof a === \"object\" && a !== null ? Object.prototype.toString.call(a) : String(a)).join(\" \")); try { write(); } finally { console.log = log; } return lines.join(\"|\"); })($0)"
    )]
    public static string Capture(Action write)
    {
        var captured = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(captured);

        try
        {
            write();
        }
        finally
        {
            Console.SetOut(previous);
        }

        return string.Join(
            "|",
            captured.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        );
    }
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

    public static string ConsoleWriteLine() =>
        BxConsole.Capture(() =>
        {
            object o = BxColor.Green;
            Console.WriteLine((object)BxColor.Red);
            Console.WriteLine(o);
            Console.WriteLine(BxPerms.Read | BxPerms.Write);
        });

    public static string ObjectListLookups()
    {
        object o = BxColor.Red;
        var colors = new List<object> { BxColor.Red, BxColor.Green };

        return colors.Contains(BxColor.Green)
            + "|"
            + colors.IndexOf(o)
            + "|"
            + colors.IndexOf(BxColor.Blue);
    }

    public static string ObjectDictionaryLookups()
    {
        var names = new Dictionary<object, string> { [BxColor.Red] = "r" };
        names[BxColor.Green] = "g";

        return names.ContainsKey(BxColor.Red)
            + "|"
            + names.ContainsKey(BxColor.Green)
            + "|"
            + names.Count;
    }

    public static string ObjectHashSetCount()
    {
        var set = new HashSet<object> { BxColor.Red, BxColor.Red, BxColor.Green };

        return set.Count.ToString();
    }

    public static string ObjectHashSetMixedCount()
    {
        var set = new HashSet<object> { BxColor.Red, 0 };

        return set.Count.ToString();
    }

    public static string UnboxToUnderlying()
    {
        object o = BxColor.Green;

        return ((int)o).ToString();
    }

    public static string UnboxToSameWidthEnum()
    {
        object o = BxColor.Green;

        return ((int)(BxPerms)o).ToString();
    }

    public static string UnboxToOtherWidthThrows()
    {
        object o = BxColor.Green;

        try
        {
            return ((long)o).ToString();
        }
        catch (InvalidCastException)
        {
            return "InvalidCastException";
        }
    }

    public static string IsIntOnABox()
    {
        object o = BxColor.Green;

        return (o is int).ToString();
    }

    public static string WideFlags()
    {
        object o = BxWide.Low | BxWide.High;

        return o.ToString() + "|" + o;
    }

    public static string HugeFlags()
    {
        object o = BxHuge.Low | BxHuge.Top;

        return o.ToString() + "|" + o;
    }

    public static string WideFlagsFromValue()
    {
        object o = (BxWide)1099511627777L;

        return o.ToString() + "|" + o;
    }

    public static string HugeFlagsFromValue()
    {
        object o = (BxHuge)9223372036854775809UL;

        return o.ToString() + "|" + o;
    }

    public static string WideFlagsCombinedUnboxed() =>
        ((long)(BxWide.Low | BxWide.High)).ToString();
}
