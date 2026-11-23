namespace Conformance;

/// <summary>A getter that counts its reads, for the read-once tuple cast row.</summary>
public class TupleHolder
{
    public int Reads;

    public ((int, int), int) Nested
    {
        get
        {
            Reads++;

            return ((1, 2), 3);
        }
    }
}

/// <summary>A record with a tuple field, for the hash row.</summary>
public record TupleFieldRecord((int, int) Pair);

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

    static string Thrown(Func<string> body)
    {
        try
        {
            return body();
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    public static string NullableTupleCastElements()
    {
        (int?, int) s = (null, 6);
        ((int, int)?, int) nested = (null, 6);
        int? n = null;

        return Thrown(() => ((int, int))s + "")
            + "|"
            + Thrown(() => ((int, long))s + "")
            + "|"
            + Thrown(() => (((int, int), long))nested + "")
            + "|"
            + Thrown(() => ((int, int))(n, 6) + "");
    }

    public static string TupleCastReadsAGetterOnce()
    {
        var holder = new TupleHolder();
        var wide = (((long, long), long))holder.Nested;

        return holder.Reads + "|" + wide.Item1.Item2 + "|" + wide.Item2;
    }

    public static string PresentNullableValues()
    {
        var c = true;
        int? n = null;
        object boxed = null;
        var nested = ((long, long))((int?)(int?)2, 1);

        return (c ? (int?)5 : (int?)6).Value
            + "|"
            + (n ?? (int?)5).Value
            + "|"
            + (c ? (int?)5 : 6).Value
            + "|"
            + nested.Item1
            + "|"
            + Thrown(() => ((long, long))((int?)boxed, 1) + "");
    }

    public static string NullableDecimalLiteral()
    {
        decimal? d = 5;

        return (d.Value / 4m).ToString();
    }

    public static string TupleDefaults()
    {
        var t = (0, 0);
        (int, int)? missing = null;

        return t.Equals(default)
            + "|"
            + missing.GetValueOrDefault().Equals((0, 0))
            + "|"
            + (
                new TupleFieldRecord((1, 2)).GetHashCode()
                == new TupleFieldRecord((3, 4)).GetHashCode()
            );
    }
}
