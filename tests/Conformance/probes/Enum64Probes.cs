namespace Conformance;

[System.Flags]
public enum E6Wide : long
{
    None = 0,
    Low = 1,
    Mid = 1L << 31,
    High = 1L << 40,
    Top = 1L << 62,
}

[System.Flags]
public enum E6Huge : ulong
{
    None = 0,
    Low = 1,
    Top = 1UL << 63,
}

public enum E6Big : ulong
{
    Zero = 0,
    Top = 1UL << 63,
    Max = ulong.MaxValue,
}

public enum E6Signed : long
{
    Min = long.MinValue,
    Minus = -1,
    Odd = (1L << 53) + 1,
    Max = long.MaxValue,
}

public enum E6NoZero : long
{
    One = 1,
    Two = 2,
}

public enum E6Small
{
    A = 1,
    B = 2,
}

public sealed class E6Holder
{
    public E6NoZero Unset;
    public E6Big Big;
}

/// <summary>
/// 64-bit enums (<c>enum E : long</c> / <c>ulong</c>) as exact <c>bigint</c>s
/// (spec 011 FR-044 as amended by #701): member values, operators, wrap-around,
/// conversions, boxing, <c>Enum</c> helpers, defaults and collections. Values
/// print through <c>(long)</c> / <c>(ulong)</c>, since an unboxed enum's
/// <c>ToString()</c> is a separate known gap.
/// </summary>
public static class Enum64Probes
{
    public static string MemberValues() =>
        (long)E6Wide.High
        + ","
        + (long)E6Wide.Top
        + ","
        + (ulong)E6Big.Top
        + ","
        + (ulong)E6Big.Max
        + ","
        + (long)E6Signed.Odd
        + ","
        + (long)E6Signed.Min;

    public static string Bitwise()
    {
        var all = E6Wide.Low | E6Wide.Mid | E6Wide.High | E6Wide.Top;
        var masked = all & ~E6Wide.Mid;
        var toggled = all ^ E6Wide.High;
        var huge = E6Huge.Low | E6Huge.Top;
        var notHuge = ~E6Huge.Low;

        return (long)all
            + "|"
            + (long)masked
            + "|"
            + (long)toggled
            + "|"
            + (long)~E6Wide.None
            + "|"
            + (ulong)huge
            + "|"
            + (ulong)notHuge
            + "|"
            + (ulong)(huge & E6Huge.Top);
    }

    public static string HasFlagAndTests()
    {
        var value = E6Wide.Low | E6Wide.High;

        return value.HasFlag(E6Wide.High)
            + ","
            + value.HasFlag(E6Wide.Top)
            + ","
            + ((value & E6Wide.High) != 0)
            + ","
            + ((value & E6Wide.Mid) == E6Wide.None)
            + ","
            + (E6Huge.Low | E6Huge.Top).HasFlag(E6Huge.Top);
    }

    public static string Arithmetic()
    {
        var max = E6Signed.Max;
        max++;
        var min = E6Signed.Min;
        min--;
        var big = E6Big.Max;
        big += 1;
        var zero = E6Big.Zero;
        zero -= 1;
        var next = E6Signed.Odd + 1;
        var back = E6Signed.Odd - 2;
        var distance = E6Signed.Max - E6Signed.Odd;

        return (long)max
            + "|"
            + (long)min
            + "|"
            + (ulong)big
            + "|"
            + (ulong)zero
            + "|"
            + (long)next
            + "|"
            + (long)back
            + "|"
            + distance;
    }

    public static string PostfixValue()
    {
        var value = E6Signed.Max;
        var before = value++;
        var big = E6Big.Zero;
        var previous = big--;

        return (long)before + "|" + (long)value + "|" + (ulong)previous + "|" + (ulong)big;
    }

    public static string ShiftsViaCast()
    {
        var high = E6Wide.High;
        var left = (E6Wide)((long)high << 22);
        var right = (E6Wide)((long)high >> 8);
        var top = (E6Huge)((ulong)E6Huge.Top >> 63);

        return (long)left + "|" + (long)right + "|" + (ulong)top;
    }

    public static string Comparisons()
    {
        var a = E6Signed.Odd;
        var b = E6Signed.Max;

        return (a < b)
            + ","
            + (a > E6Signed.Minus)
            + ","
            + (E6Big.Max > E6Big.Top)
            + ","
            + (a == E6Signed.Odd)
            + ","
            + (b != E6Signed.Max)
            + ","
            + (E6Signed.Min <= E6Signed.Minus);
    }

    static string Label(E6Signed value)
    {
        switch (value)
        {
            case E6Signed.Min:
                return "min";
            case E6Signed.Odd:
                return "odd";
            case E6Signed.Max:
                return "max";
            default:
                return "other";
        }
    }

    static string Label(E6Big value) =>
        value switch
        {
            E6Big.Zero => "zero",
            E6Big.Top => "top",
            E6Big.Max => "max",
            _ => "other",
        };

    public static string Switches() =>
        Label(E6Signed.Odd)
        + ","
        + Label((E6Signed)long.MaxValue)
        + ","
        + Label((E6Signed)5)
        + ","
        + Label(E6Big.Top)
        + ","
        + Label((E6Big)ulong.MaxValue)
        + ","
        + Label((E6Big)7);

    public static string Casts()
    {
        var odd = E6Signed.Odd;
        var top = E6Wide.Top;
        var mid = E6Wide.Mid;
        var minus = E6Signed.Minus;
        var max = E6Big.Max;
        var topBig = E6Big.Top;
        var negativeInt = -5;
        var negativeLong = -1L;
        var bigLong = 1099511627777L;
        var small = E6Small.B;
        var fraction = 3.9;
        int narrow = (int)top;
        int narrowOdd = (int)odd;
        ulong unsigned = (ulong)minus;
        long signed = (long)max;
        double asDouble = (double)topBig;
        var fromInt = (E6Signed)negativeInt;
        var fromNegative = (E6Big)negativeLong;
        var fromLong = (E6Wide)bigLong;
        var fromSmall = (E6Wide)small;
        var toSmall = (E6Small)mid;
        var fromDouble = (E6Signed)fraction;

        return narrow
            + "|"
            + narrowOdd
            + "|"
            + unsigned
            + "|"
            + signed
            + "|"
            + asDouble
            + "|"
            + (long)fromInt
            + "|"
            + (ulong)fromNegative
            + "|"
            + (long)fromLong
            + "|"
            + (long)fromSmall
            + "|"
            + (int)toSmall
            + "|"
            + (long)fromDouble;
    }

    public static string CheckedCasts()
    {
        var results = new List<string>();
        var top = E6Wide.Top;
        var max = E6Big.Max;
        var minus = -1L;

        try
        {
            results.Add(checked((int)top).ToString());
        }
        catch (Exception e)
        {
            results.Add(ProbeUtil.Kind(e));
        }

        try
        {
            results.Add(checked((long)max).ToString());
        }
        catch (Exception e)
        {
            results.Add(ProbeUtil.Kind(e));
        }

        try
        {
            results.Add(((ulong)checked((E6Big)minus)).ToString());
        }
        catch (Exception e)
        {
            results.Add(ProbeUtil.Kind(e));
        }

        try
        {
            var value = E6Signed.Max;
            value = checked(value + 1);
            results.Add(((long)value).ToString());
        }
        catch (Exception e)
        {
            results.Add(ProbeUtil.Kind(e));
        }

        return string.Join(",", results);
    }

    public static string Boxing()
    {
        object boxed = E6Wide.Low | E6Wide.High;
        object huge = E6Big.Max;
        object other = E6Wide.High;

        return (long)(E6Wide)boxed
            + "|"
            + (long)boxed
            + "|"
            + (ulong)huge
            + "|"
            + (boxed is E6Wide)
            + "|"
            + (huge is E6Wide)
            + "|"
            + boxed.Equals(E6Wide.Low | E6Wide.High)
            + "|"
            + boxed.Equals(other)
            + "|"
            + huge
            + "|"
            + $"{boxed}"
            + "|"
            + boxed.GetType().Name;
    }

    public static string Hashes() =>
        E6Wide.High.GetHashCode()
        + ","
        + E6Big.Max.GetHashCode()
        + ","
        + E6Signed.Odd.GetHashCode()
        + ","
        + ((object)E6Signed.Min).GetHashCode()
        + ","
        + (((object)E6Big.Top).GetHashCode() == E6Big.Top.GetHashCode());

    public static string EnumHelpers()
    {
        var parsed = Enum.Parse<E6Big>("Top");
        var numeric = Enum.Parse<E6Big>("9223372036854775809");
        var combined = Enum.Parse<E6Wide>("Low, High");
        string overflow;

        try
        {
            overflow = ((long)Enum.Parse<E6Signed>("9223372036854775808")).ToString();
        }
        catch (Exception e)
        {
            overflow = ProbeUtil.Kind(e);
        }

        return Enum.IsDefined(E6Big.Max)
            + "|"
            + Enum.IsDefined((E6Big)5)
            + "|"
            + Enum.IsDefined(E6Wide.Low | E6Wide.High)
            + "|"
            + (ulong)parsed
            + "|"
            + (ulong)numeric
            + "|"
            + (long)combined
            + "|"
            + overflow;
    }

    public static string Defaults()
    {
        E6NoZero local = default;
        var array = new E6NoZero[2];
        var holder = new E6Holder();
        E6Big zero = 0;

        return (long)local
            + "|"
            + (long)array[1]
            + "|"
            + (long)holder.Unset
            + "|"
            + (ulong)holder.Big
            + "|"
            + (ulong)zero
            + "|"
            + (zero == E6Big.Zero);
    }

    static E6Wide? WithLow(E6Wide? value) => value | E6Wide.Low;

    static E6Signed? Next(E6Signed? value) => value + 1;

    public static string Nullables()
    {
        var some = WithLow(E6Wide.High);
        var none = WithLow(null);
        var next = Next(E6Signed.Max);

        return (long)some!.Value
            + "|"
            + (none is null)
            + "|"
            + (long)next!.Value
            + "|"
            + (Next(null) is null);
    }

    public static string Collections()
    {
        var map = new Dictionary<E6Big, string> { [E6Big.Max] = "max", [E6Big.Top] = "top" };
        var set = new HashSet<E6Wide> { E6Wide.High, E6Wide.Top };
        var list = new List<E6Signed> { E6Signed.Odd, E6Signed.Min, E6Signed.Max };

        return map[E6Big.Max]
            + "|"
            + map.ContainsKey((E6Big)ulong.MaxValue)
            + "|"
            + set.Contains(E6Wide.High)
            + "|"
            + set.Contains(E6Wide.Mid)
            + "|"
            + list.Contains(E6Signed.Max)
            + "|"
            + (long)list.Max()
            + "|"
            + (long)list.Min()
            + "|"
            + string.Join(",", list.OrderBy(value => value).Select(value => (long)value))
            + "|"
            + list.Sum(value => (long)value / 4);
    }
}
