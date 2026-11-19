namespace Conformance;

[System.Flags]
public enum TextAccess
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}

public static class DefaultValueTextProbes
{
    public static string EnumNames()
    {
        var flags = TextAccess.Read | TextAccess.Write;
        TextAccess? absent = null;
        TextAccess? present = flags;
        object boxed = flags;
        return flags.ToString()
            + "|"
            + $"{flags}"
            + "|"
            + flags
            + "|"
            + absent.ToString()
            + "|"
            + present.ToString()
            + "|"
            + boxed.ToString()
            + "|"
            + ((TextAccess)8).ToString()
            + "|"
            + TextAccess.None.ToString();
    }

    public static string EnumJoin()
    {
        return string.Join(";", new[] { TextAccess.Read, TextAccess.Write })
            + "|"
            + string.Join(";", TextAccess.Read, TextAccess.Write)
            + "|"
            + $"{TextAccess.Read:D}";
    }

    public static string ErasedPrimitives()
    {
        object on = true;
        object ratio = 1e-7;
        return on.ToString()
            + "|"
            + ratio.ToString()
            + "|"
            + $"{on}{ratio}"
            + "|"
            + Generic(true)
            + "|"
            + Generic(1e-7)
            + "|"
            + string.Join(";", new object?[] { on, ratio, null });
    }

    public static string NullNullableText()
    {
        bool? on = null;
        double? ratio = null;
        int? count = null;
        return "[" + on.ToString() + "|" + ratio.ToString() + "|" + count.ToString() + "]";
    }

    public static string DurationText()
    {
        var ticks = TimeSpan.FromTicks(-864000000001L);
        TimeSpan? absent = null;
        object boxed = ticks;
        return ticks.ToString()
            + "|"
            + $"{ticks}"
            + "|"
            + ticks
            + "|"
            + absent.ToString()
            + "|"
            + boxed.ToString()
            + "|"
            + Generic(ticks)
            + "|"
            + TimeSpan.Zero.ToString();
    }

    public static string TupleText()
    {
        return (1, "a").ToString()
            + "|"
            + (true, 1e-7, (1, "a")).ToString()
            + "|"
            + ((string?)null, false).ToString();
    }

    public static string DecimalScaleText()
    {
        var amount = 2.50m;
        object boxed = amount;
        string? text = null;
        text += amount;
        return "v="
            + amount
            + "|"
            + $"{amount}"
            + "|"
            + boxed.ToString()
            + "|"
            + Generic(amount)
            + "|"
            + text
            + "|"
            + 0.00m;
    }

    private static string Generic<T>(T value) => value + "/" + $"{value}" + "/" + value?.ToString();
}
