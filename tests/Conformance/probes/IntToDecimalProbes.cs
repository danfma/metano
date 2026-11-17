namespace Conformance;

public readonly record struct DecCash
{
    public decimal A { get; }

    public DecCash(int cents)
    {
        A = cents;
    }
}

public class DecHolder
{
    public decimal Field = 3;

    public decimal Prop { get; set; } = (byte)4;
}

/// <summary>
/// Implicit and explicit conversions from the integers of 32 bits or fewer
/// to <c>decimal</c> (baseline FR-080, #476): each converted value is a
/// <c>decimal</c>, so a <c>decimal</c> operation on it runs.
/// </summary>
public static class IntToDecimalProbes
{
    static decimal Half(decimal d) => d / 2m;

    static decimal FromInt(int i) => i;

    public static string Assignments()
    {
        int i = 7;
        decimal d = i;
        decimal e;
        e = (short)-3;
        var cash = new DecCash(5);
        var holder = new DecHolder();

        return (d + 0.5m)
            + "|"
            + (e * 2m)
            + "|"
            + (cash.A + 0.25m)
            + "|"
            + (holder.Field + 0.5m)
            + "|"
            + (holder.Prop + 0.5m);
    }

    public static string ArgumentsAndReturns()
    {
        int i = 9;
        uint u = 4294967295;
        ushort s = 65535;

        return Half(i) + "|" + Half(u) + "|" + Half(s) + "|" + (FromInt(3) + 0.5m);
    }

    public static string Operators()
    {
        decimal d = 2.5m;
        int i = 3;
        sbyte sb = -2;

        return (d + i)
            + "|"
            + (i * d)
            + "|"
            + (d - sb)
            + "|"
            + (i < d ? "T" : "F")
            + "|"
            + (d > i ? "T" : "F")
            + "|"
            + (i == 3m ? "T" : "F");
    }

    public static string Compound()
    {
        decimal d = 1.5m;
        int i = 2;
        byte b = 3;
        d += i;
        d -= b;
        d *= i;
        d /= 4;
        d += 1;

        return (d + 0.3m).ToString();
    }

    public static string CompoundTargets()
    {
        var holder = new DecHolder();
        decimal[] arr = [1m, 2m];
        decimal? n = 2.5m;
        int i = 3;
        holder.Field += i;
        holder.Prop -= 1;
        arr[1] *= i;
        n += i;

        return (holder.Field + 0.5m)
            + "|"
            + (holder.Prop + 0.5m)
            + "|"
            + (arr[1] + 0.5m)
            + "|"
            + (n + 0.25m);
    }

    public static string Increments()
    {
        decimal d = 1.5m;
        var post = d++;
        var pre = ++d;
        decimal[] arr = [0.5m];
        var i = 0;
        arr[i++]--;
        decimal? n = 0.25m;
        n++;

        return post + "|" + pre + "|" + d + "|" + arr[0] + "|" + i + "|" + n;
    }

    public static string NegativeZero()
    {
        int a = -4,
            b = 2;
        decimal m = a % b;
        decimal p = a * 0;
        int? n = a % b;
        decimal? q = n;

        return m + "|" + p + "|" + q + "|" + (m + 0m);
    }

    public static string Nullables()
    {
        int? n = 4;
        int? none = null;
        decimal? a = n;
        decimal? z = none;
        decimal c = n ?? 0m;
        decimal f = none ?? 1.5m;

        return (a + 0.5m) + "|" + (z == null ? "null" : "x") + "|" + (c + 0.5m) + "|" + (f + 0.25m);
    }

    public static string Casts()
    {
        int i = -11;
        uint u = 4000000000;

        return ((decimal)i + 0.5m) + "|" + ((decimal)u + 0.5m) + "|" + ((decimal?)(int?)6 + 0.5m);
    }

    public static string Elements()
    {
        int i = 2;
        decimal[] arr = [i, 3, 1.5m];
        decimal pick = i > 0 ? i : 0.5m;
        Func<int, decimal> f = x => x;

        return (arr[0] + arr[1] + arr[2]) + "|" + (pick + 0.5m) + "|" + (f(4) + 0.5m);
    }
}
