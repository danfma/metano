namespace Conformance;

public static class ParseProbes
{
    static string P(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Exception e)
        {
            return ProbeUtil.Kind(e);
        }
    }

    public static string IntOk() =>
        P(() => int.Parse("42").ToString())
        + "|"
        + P(() => int.Parse(" 42 ").ToString())
        + "|"
        + P(() => int.Parse("+5").ToString())
        + "|"
        + P(() => int.Parse("-0").ToString())
        + "|"
        + P(() => int.Parse("007").ToString())
        + "|"
        + P(() => int.Parse("-2147483648").ToString())
        + "|"
        + P(() => int.Parse("\t9\n").ToString());

    public static string IntBad() =>
        P(() => int.Parse("1e3").ToString())
        + "|"
        + P(() => int.Parse("0x1F").ToString())
        + "|"
        + P(() => int.Parse("").ToString())
        + "|"
        + P(() => int.Parse("1.0").ToString())
        + "|"
        + P(() => int.Parse("2147483648").ToString())
        + "|"
        + P(() => int.Parse("1,000").ToString())
        + "|"
        + P(() => int.Parse("12abc").ToString())
        + "|"
        + P(() => int.Parse(" ").ToString())
        + "|"
        + P(() => int.Parse("1 2").ToString())
        + "|"
        + P(() => int.Parse("99999999999999999999").ToString())
        + "|"
        + P(() => int.Parse("- 5").ToString())
        + "|"
        + P(() => int.Parse("١٢").ToString());

    public static string IntNull() =>
        P(() =>
        {
            string? s = null;
            return int.Parse(s!).ToString();
        });

    public static string IntTry()
    {
        var a = int.TryParse("12", out var x);
        var b = int.TryParse("x", out var y);
        var c = int.TryParse((string?)null, out var z);
        var d = int.TryParse("3000000000", out var w);
        var e = int.TryParse(" 7 ", out var v);
        var f = int.TryParse("1.5", out var u);
        return a
            + "|"
            + x
            + "|"
            + b
            + "|"
            + y
            + "|"
            + c
            + "|"
            + z
            + "|"
            + d
            + "|"
            + w
            + "|"
            + e
            + "|"
            + v
            + "|"
            + f
            + "|"
            + u;
    }

    public static string Narrow() =>
        P(() => byte.Parse("255").ToString())
        + "|"
        + P(() => byte.Parse("256").ToString())
        + "|"
        + P(() => byte.Parse("-1").ToString())
        + "|"
        + P(() => sbyte.Parse("-128").ToString())
        + "|"
        + P(() => short.Parse("32768").ToString())
        + "|"
        + P(() => ushort.Parse("65535").ToString())
        + "|"
        + P(() => uint.Parse("4294967295").ToString())
        + "|"
        + P(() => uint.Parse("-1").ToString())
        + "|"
        + P(() => long.Parse("9223372036854775807").ToString())
        + "|"
        + P(() => long.Parse("9223372036854775808").ToString())
        + "|"
        + P(() => short.Parse(" 5 ").ToString())
        + "|"
        + P(() => byte.Parse("x").ToString());

    public static string NarrowTry()
    {
        var a = byte.TryParse("256", out var x);
        var b = short.TryParse("-5", out var y);
        var c = long.TryParse("12", out var z);
        var d = uint.TryParse("-1", out var w);
        return a + "|" + x + "|" + b + "|" + y + "|" + c + "|" + z + "|" + d + "|" + w;
    }

    public static string Hex() =>
        P(() => int.Parse("1F", System.Globalization.NumberStyles.HexNumber).ToString())
        + "|"
        + P(() => int.Parse("ff", System.Globalization.NumberStyles.HexNumber).ToString())
        + "|"
        + P(() => int.Parse("FFFFFFFF", System.Globalization.NumberStyles.HexNumber).ToString())
        + "|"
        + P(() => int.Parse("0x1F", System.Globalization.NumberStyles.HexNumber).ToString())
        + "|"
        + P(() => long.Parse("FF", System.Globalization.NumberStyles.HexNumber).ToString());

    public static string DoubleOk() =>
        P(() => double.Parse("1.5").ToString())
        + "|"
        + P(() => double.Parse(" 1e3 ").ToString())
        + "|"
        + P(() => double.Parse("1,000.5").ToString())
        + "|"
        + P(() => double.Parse("-.5").ToString())
        + "|"
        + P(() => double.Parse("5.").ToString())
        + "|"
        + P(() => double.Parse("Infinity").ToString())
        + "|"
        + P(() => double.Parse("-Infinity").ToString())
        + "|"
        + P(() => double.Parse("NaN").ToString())
        + "|"
        + P(() => double.Parse("1E-7").ToString())
        + "|"
        + P(() => double.Parse("+3").ToString())
        + "|"
        + P(() => double.Parse("1e400").ToString())
        + "|"
        + P(() => double.Parse("∞").ToString());

    public static string DoubleBad() =>
        P(() => double.Parse("").ToString())
        + "|"
        + P(() => double.Parse("abc").ToString())
        + "|"
        + P(() => double.Parse("1.2.3").ToString())
        + "|"
        + P(() => double.Parse("0x10").ToString())
        + "|"
        + P(() => double.Parse("1 000").ToString())
        + "|"
        + P(() => double.Parse("infinity").ToString())
        + "|"
        + P(() => double.Parse("nan").ToString())
        + "|"
        + P(() => double.Parse("1e").ToString())
        + "|"
        + P(() => double.Parse("$5").ToString())
        + "|"
        + P(() => double.Parse("(5)").ToString())
        + "|"
        + P(() => double.Parse("1_000").ToString());

    public static string DoubleTry()
    {
        var a = double.TryParse("1.5", out var x);
        var b = double.TryParse("x", out var y);
        var c = double.TryParse((string?)null, out var z);
        var d = double.TryParse("", out var w);
        return a + "|" + x + "|" + b + "|" + y + "|" + c + "|" + z + "|" + d + "|" + w;
    }

    public static string FloatParse() =>
        P(() => float.Parse("0.1").ToString())
        + "|"
        + P(() => float.Parse("1e39").ToString())
        + "|"
        + P(() => float.Parse("16777217").ToString())
        + "|"
        + P(() => float.Parse("x").ToString());

    public static string BoolParse() =>
        P(() => bool.Parse("true").ToString())
        + "|"
        + P(() => bool.Parse(" TRUE ").ToString())
        + "|"
        + P(() => bool.Parse("False").ToString())
        + "|"
        + P(() => bool.Parse("1").ToString())
        + "|"
        + P(() => bool.Parse("yes").ToString())
        + "|"
        + P(() => bool.Parse("").ToString())
        + "|"
        + P(() =>
        {
            string? s = null;
            return bool.Parse(s!).ToString();
        })
        + "|"
        + (bool.TryParse("tRuE", out var t) && t)
        + "|"
        + bool.TryParse("x", out var u)
        + u
        + "|"
        + bool.TryParse((string?)null, out var v);

    public static string DecimalParse() =>
        P(() => decimal.Parse("1.50").ToString())
        + "|"
        + P(() => decimal.Parse(" 1,234.5 ").ToString())
        + "|"
        + P(() => decimal.Parse("1e3").ToString())
        + "|"
        + P(() => decimal.Parse("").ToString())
        + "|"
        + P(() => decimal.Parse("-.5").ToString())
        + "|"
        + P(() => decimal.Parse("x").ToString())
        + "|"
        + P(() => decimal.Parse("79228162514264337593543950336").ToString())
        + "|"
        + P(() => decimal.Parse("NaN").ToString())
        + "|"
        + P(() => decimal.Parse("Infinity").ToString())
        + "|"
        + P(() => decimal.Parse("0x10").ToString());

    public static string DecimalTry()
    {
        var r = "";
        foreach (
            var s in new[]
            {
                "1.50",
                "1e3",
                "",
                " 2 ",
                "abc",
                "NaN",
                "Infinity",
                "0x1",
                "1,5",
                "+7",
                "5.",
                ".5",
                "1 2",
                "-",
                "0b1",
                "0o7",
            }
        )
        {
            r += (decimal.TryParse(s, out var v) ? v.ToString() : "F") + ";";
        }
        return r;
    }

    public static string GuidParse() =>
        P(() => Guid.Parse("11111111-2222-3333-4444-555555555555").ToString())
        + "|"
        + P(() => Guid.Parse("AAAAAAAABBBBCCCCDDDDEEEEEEEEEEEE").ToString())
        + "|"
        + P(() => Guid.Parse("{11111111-2222-3333-4444-555555555555}").ToString())
        + "|"
        + P(() => Guid.Parse("(11111111-2222-3333-4444-555555555555)").ToString())
        + "|"
        + P(() => Guid.Parse("nope").ToString())
        + "|"
        + P(() => Guid.Parse("").ToString())
        + "|"
        + P(() => Guid.Parse(" 11111111-2222-3333-4444-555555555555 ").ToString())
        + "|"
        + P(() => Guid.Parse("11111111-2222-3333-4444-55555555555").ToString())
        + "|"
        + Guid.TryParse("zz", out var g)
        + "|"
        + Guid.TryParse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE", out var h)
        + h
        + "|"
        + (Guid.Empty == default(Guid))
        + "|"
        + (Guid.NewGuid() != Guid.NewGuid())
        + "|"
        + Guid.NewGuid().ToString().Length;

    public static string TimeSpans()
    {
        return TimeSpan.FromSeconds(1.5).TotalMilliseconds.ToString()
            + "|"
            + TimeSpan.FromMinutes(2).TotalSeconds.ToString()
            + "|"
            + TimeSpan.FromHours(1.5).TotalMinutes.ToString()
            + "|"
            + TimeSpan.FromDays(1).TotalHours.ToString()
            + "|"
            + TimeSpan.FromMilliseconds(1.5).Ticks
            + "|"
            + new TimeSpan(1, 2, 3).TotalSeconds.ToString()
            + "|"
            + new TimeSpan(1, 2, 3, 4).TotalSeconds.ToString()
            + "|"
            + new TimeSpan(1, 2, 3, 4, 5).TotalMilliseconds.ToString()
            + "|"
            + new TimeSpan(10_000_000).TotalSeconds.ToString()
            + "|"
            + TimeSpan.FromTicks(5).Ticks
            + "|"
            + TimeSpan.FromSeconds(1).Add(TimeSpan.FromSeconds(2)).TotalSeconds.ToString()
            + "|"
            + TimeSpan.FromSeconds(90).Minutes
            + "|"
            + TimeSpan.FromSeconds(90).Seconds
            + "|"
            + TimeSpan.FromSeconds(0.0000001).Ticks
            + "|"
            + TimeSpan.FromSeconds(1, 500).TotalMilliseconds.ToString();
    }

    public static string TimeSpanOverflow() =>
        P(() => TimeSpan.FromDays(1e10).ToString())
        + "|"
        + P(() => TimeSpan.FromSeconds(double.NaN).ToString())
        + "|"
        + P(() => TimeSpan.FromSeconds(double.PositiveInfinity).ToString());

    public static string Offsets()
    {
        var o = new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.FromHours(2));
        var u = new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.Zero);
        var d = u;
        return o.ToUnixTimeSeconds()
            + "|"
            + u.ToUnixTimeSeconds()
            + "|"
            + o.Offset.TotalHours.ToString()
            + "|"
            + o.Hour
            + "|"
            + o.UtcDateTime.Hour
            + "|"
            + d.ToUnixTimeMilliseconds()
            + "|"
            + (o < u ? "T" : "F")
            + "|"
            + o.Year
            + "-"
            + o.Month
            + "-"
            + o.Day
            + "|"
            + new DateTimeOffset(
                2024,
                3,
                5,
                10,
                20,
                30,
                123,
                TimeSpan.FromMinutes(-90)
            ).ToUnixTimeMilliseconds();
    }

    public static string OffsetBad() =>
        P(() => new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.FromHours(15)).ToString())
        + "|"
        + P(() => new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.FromSeconds(30)).ToString())
        + "|"
        + P(() => new DateTimeOffset(2024, 13, 5, 10, 20, 30, TimeSpan.Zero).ToString());

    public static string ConvertCalls() =>
        P(() => Convert.ToInt32("42").ToString())
        + "|"
        + P(() => Convert.ToInt32("x").ToString())
        + "|"
        + P(() => Convert.ToDouble("1.5").ToString())
        + "|"
        + P(() => Convert.ToBoolean("true").ToString());

    public static string LongParse() =>
        P(() => long.Parse(" 12 ").ToString())
        + "|"
        + P(() => long.Parse("x").ToString())
        + "|"
        + P(() => long.Parse("1.0").ToString())
        + "|"
        + P(() => long.Parse("-9223372036854775808").ToString())
        + "|"
        + P(() => ulong.Parse("18446744073709551615").ToString())
        + "|"
        + P(() => ulong.Parse("-1").ToString());
}
