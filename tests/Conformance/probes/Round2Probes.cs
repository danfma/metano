namespace Conformance;

public struct PlainS : IShape
{
    public int A;

    public PlainS(int a)
    {
        A = a;
    }
}

public record struct RecS(int A) : IShape;

public record AnyBox(object? V);

public record IfaceBox(IShape? S);

public class Money3 : Money2
{
    public Money3(int c)
        : base(c) { }

    public override string ToString() => "M3:" + Cents;
}

public class Counter
{
    public int Calls;

    public string? Next()
    {
        Calls++;
        return Calls % 2 == 1 ? "abcdef" : null;
    }

    public Person? NextPerson()
    {
        Calls++;
        return new Person();
    }

    public override string ToString() => "Counter" + Calls;

    public string Self() => "<" + this + ">";

    public string SelfI() => $"<{this}>";
}

public static class Round2Probes
{
    static string B(bool b) => b ? "T" : "F";

    static decimal Dec(string s) => decimal.Parse(s);

    static string N(object? o) => o == null ? "null" : o.ToString()!;

    // #308 x decimal->double casts
    public static string DecToDoubleConst()
    {
        return "v" + (double)1.25m;
    }

    public static string DecToDoubleVar()
    {
        decimal m = Dec("1.25");
        double d = (double)m;
        return "v"
            + d
            + "|"
            + (double)m
            + "|"
            + $"{(double)m}"
            + "|"
            + ((double)m).ToString()
            + "|"
            + (d + 1);
    }

    public static string DecToDoubleThird()
    {
        decimal m = Dec("1") / 3m;
        double d = (double)m;
        return d.ToString() + "|" + (d * 3 == 1.0 ? "T" : "F");
    }

    public static string DecToFloat()
    {
        decimal m = Dec("0.1");
        float f = (float)m;
        return "v" + f + "|" + (float)m;
    }

    public static string DecToIntConcat()
    {
        decimal m = Dec("2.9");
        return "v" + (int)m + "|" + (long)m;
    }

    public static string DoubleExprs()
    {
        int i = 3;
        double d = 0.1;
        var xs = new[] { 1.5, 2.5 };
        double? n = 2.5;
        return "v"
            + i / 2.0
            + "|"
            + (double)i
            + "|"
            + (i > 2 ? 1.5 : 2)
            + "|"
            + xs.Average()
            + "|"
            + Math.Round(d + 0.2, 2)
            + "|"
            + n.Value
            + "|"
            + (float)(d + 0.2)
            + "|"
            + Math.Sqrt(2)
            + "|"
            + (d + 0.2)
            + "|"
            + xs[0]
            + "|"
            + Math.Floor(2.5)
            + "|"
            + 1e21 * 10;
    }

    public static string BoolExprs()
    {
        int i = 3;
        var xs = new List<int> { 1 };
        string? s = null;
        return "v"
            + (i > 2)
            + "|"
            + xs.Contains(1)
            + "|"
            + string.IsNullOrEmpty(s)
            + "|"
            + (s == null)
            + "|"
            + xs.Any()
            + "|"
            + !xs.Any()
            + "|"
            + (i is 3)
            + "|"
            + "abc".StartsWith("a")
            + "|"
            + (true ? "y" : "n")
            + "|"
            + bool.Parse("true");
    }

    public static string NullableText()
    {
        bool? b = true;
        double? d = 1e-7;
        float? f = 0.1f;
        return b.ToString()
            + "|"
            + d.ToString()
            + "|"
            + f.ToString()
            + "|"
            + b
            + d
            + f
            + "|"
            + $"{b}{d}{f}"
            + "|"
            + (b?.ToString() ?? "n")
            + (d?.ToString() ?? "n");
    }

    public static string NullableTextNull()
    {
        bool? b = null;
        double? d = null;
        return "[" + b.ToString() + "|" + d.ToString() + "]" + b + d;
    }

    // #674
    public static string IfaceStruct() =>
        B(new IfaceBox(new PlainS(1)) == new IfaceBox(new PlainS(1)))
        + B(new IfaceBox(new RecS(1)) == new IfaceBox(new RecS(1)))
        + B(new IfaceBox(new PlainS(1)) == new IfaceBox(new PlainS(2)))
        + B(new IfaceBox(new Circle(1)) == new IfaceBox(new Circle(1)));

    public static string ObjStruct() =>
        B(new AnyBox(new PlainS(1)) == new AnyBox(new PlainS(1)))
        + B(new AnyBox(new RecS(1)) == new AnyBox(new RecS(1)))
        + B(new AnyBox((1, "a")) == new AnyBox((1, "a")))
        + B(new AnyBox(new PlainS(1)) == new AnyBox(new PlainS(2)))
        + B(new AnyBox(5L) == new AnyBox(5L))
        + B(new AnyBox(1.5m) == new AnyBox(1.5m))
        + B(new AnyBox(Guid.Empty) == new AnyBox(Guid.Empty))
        + B(new AnyBox(new DateTime(2020, 1, 1)) == new AnyBox(new DateTime(2020, 1, 1)))
        + B(new AnyBox(TimeSpan.FromSeconds(1)) == new AnyBox(TimeSpan.FromSeconds(1)))
        + B(new AnyBox(Color.Red) == new AnyBox(Color.Red))
        + B(new AnyBox('a') == new AnyBox('a'))
        + B(new AnyBox(true) == new AnyBox(true))
        + B(new AnyBox(new DateOnly(2020, 1, 1)) == new AnyBox(new DateOnly(2020, 1, 1)));

    public static string GenericStructs() =>
        B(new Box<PlainS>(new PlainS(1)) == new Box<PlainS>(new PlainS(1)))
        + B(new Box<RecS>(new RecS(1)) == new Box<RecS>(new RecS(1)))
        + B(
            new Box<TimeSpan>(TimeSpan.FromSeconds(1)) == new Box<TimeSpan>(TimeSpan.FromSeconds(1))
        )
        + B(
            new Box<DateOnly>(new DateOnly(2020, 1, 1))
                == new Box<DateOnly>(new DateOnly(2020, 1, 1))
        )
        + B(new Box<char>('a') == new Box<char>('a'))
        + B(new Box<int[]?>(null) == new Box<int[]?>(null));

    public static string WithAndHash()
    {
        var a = new Box<Inner>(new Inner("x"));
        var b = a with { };
        var c = a with { Value = new Inner("x") };
        return B(a == b)
            + B(a == c)
            + B(a.GetHashCode() == c.GetHashCode())
            + B(new Box<double>(double.NaN).Equals(new Box<double>(double.NaN)))
            + B(new HashSet<Box<Inner>> { a }.Contains(c));
    }

    // #677 inheritance over dispatched Equals/ToString

    // #661 receiver evaluation count
    public static string CondReceiverOnce()
    {
        var c = new Counter();
        var a = c.Next()?.Substring(1, 2);
        var b = c.Next()?.Substring(1, 2);
        var d = c.Next()?.Trim().ToUpper().Substring(0, 1);
        return N(a) + "|" + N(b) + "|" + N(d) + "|" + c.Calls;
    }

    public static string CondReceiverMember()
    {
        var c = new Counter();
        var a = c.NextPerson()?.Name.Substring(1);
        var b = c.NextPerson()?.Nick?.Substring(1);
        var e = c.NextPerson()?.Name?.PadLeft(7, '.').Substring(0, 3);
        return N(a) + "|" + N(b) + "|" + N(e) + "|" + c.Calls;
    }

    public static string CondIndexThenString()
    {
        var xs = new List<string?> { "hello", null };
        var i = 0;
        var a = xs[i++]?.Substring(1);
        var b = xs[i++]?.Substring(1);
        return N(a) + "|" + N(b) + "|" + i;
    }

    public static string StringHelpersPlain()
    {
        var c = new Counter();
        return c.Next()!.Substring(2)
            + "|"
            + c.Calls
            + "|"
            + "abc".Substring(1).Length
            + "|"
            + "a,b".Split(',')[1].PadLeft(3, '_')
            + "|"
            + " x ".Trim().Insert(1, "y").Remove(0, 1);
    }

    // #649 forms
    public static string ThisConcat()
    {
        var c = new Counter();
        return c.Self() + c.SelfI();
    }

    public static string TypedLocals()
    {
        string s = "x";
        string? n = null;
        object o = 5;
        return "a"
            + s
            + n
            + o
            + s.Length
            + s.ToUpper()
            + (s + n)
            + (n ?? s)
            + (n ?? "lit")
            + (object?)n
            + (string?)null;
    }

    public static string Params() => Par("p", null, 3, null);

    static string Par(string a, string? b, int c, int? d) => a + b + c + d + $"{a}{b}{c}{d}";

    public static string BangMember()
    {
        Holder? h = new Holder();
        string? s = "q";
        return "a" + h!.Name + h!.MaybeName + s!.Length + s!.ToUpper() + h.Prop! + (h.MaybeName!);
    }

    public static string SwitchExpr()
    {
        int k = 2;
        string? n = null;
        return "a"
            + (
                k switch
                {
                    1 => "one",
                    2 => n,
                    _ => "many",
                }
            )
            + "b"
            + (
                k switch
                {
                    1 => "one",
                    _ => "many",
                }
            );
    }

    public static string AssignExpr()
    {
        string? s;
        int i = 0;
        return "a" + (s = null) + (i = 5) + i++ + -i + (s = "z");
    }

    public static string NestedInterp()
    {
        string? n = null;
        int? k = null;
        double? d = null;
        return $"[{n}|{k}|{d}|{k:D3}|{d:F2}|{n ?? "x"}|{(n == null ? "null" : n)}|{n?.Length}|{k + 1}]";
    }

    public static string ExCtorArgs()
    {
        string? n = null;
        var r = "";
        try
        {
            throw new ArgumentNullException(n);
        }
        catch (ArgumentNullException e)
        {
            r += (e.ParamName ?? "noparam") + ";";
        }
        try
        {
            throw new ArgumentException(n, "p");
        }
        catch (ArgumentException e)
        {
            r += e.ParamName + ";";
        }
        try
        {
            throw new ArgumentException("m", n);
        }
        catch (ArgumentException e)
        {
            r += e.Message + (e.ParamName ?? "noparam") + ";";
        }
        try
        {
            throw new InvalidOperationException(n, new Exception("in"));
        }
        catch (Exception e)
        {
            r += e.InnerException!.Message + ";";
        }
        try
        {
            throw new NotSupportedException(n);
        }
        catch (Exception e)
        {
            r += e.Message + ";";
        }
        try
        {
            throw new KeyNotFoundException(n);
        }
        catch (Exception e)
        {
            r += e.Message + ";";
        }
        try
        {
            throw new FormatException(n);
        }
        catch (Exception e)
        {
            r += e.Message + ";";
        }
        try
        {
            throw new AppError(n!);
        }
        catch (Exception e)
        {
            r += e.Message + ";";
        }
        try
        {
            throw new Exception("has" + n);
        }
        catch (Exception e)
        {
            r += e.Message + ";";
        }
        try
        {
            throw new Exception($"i{n}");
        }
        catch (Exception e)
        {
            r += e.Message + ";";
        }
        return r;
    }

    // #651 extras
    public static string JsErrorRethrow()
    {
        string? s = null;
        try
        {
            try
            {
                return s!.Length.ToString();
            }
            catch (Exception)
            {
                throw;
            }
        }
        catch (NullReferenceException)
        {
            return "nre";
        }
        catch (Exception)
        {
            return "other";
        }
    }

    public static string JsErrorThrowE()
    {
        string? s = null;
        try
        {
            try
            {
                return s!.Length.ToString();
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        catch (NullReferenceException)
        {
            return "nre";
        }
        catch (Exception)
        {
            return "other";
        }
    }

    public static string JsErrorWrapped()
    {
        string? s = null;
        try
        {
            try
            {
                return s!.Length.ToString();
            }
            catch (Exception e)
            {
                throw new AppError("w", e);
            }
        }
        catch (AppError e)
        {
            return e.Message + (e.InnerException is NullReferenceException ? ":nre" : ":other");
        }
    }

    public static string CatchNoVarNoRead()
    {
        var r = "";
        try
        {
            throw new AppError("a");
        }
        catch (Exception)
        {
            r += "1";
        }
        try
        {
            throw new AppError("a");
        }
        catch (AppError)
        {
            r += "2";
        }
        try
        {
            throw new AppError("a");
        }
        catch (Exception e)
        {
            r += "3";
        }
        try
        {
            throw new AppError("a");
        }
        catch
        {
            r += "4";
        }
        return r;
    }

    public static string CatchPassToFn()
    {
        try
        {
            throw new AppError("a");
        }
        catch (Exception e)
        {
            return Describe(e);
        }
    }

    static string Describe(Exception e) => e.Message + (e is AppError ? "!" : "?");

    public static string FilterThrows()
    {
        string? s = null;
        try
        {
            throw new AppError("a");
        }
        catch (AppError e) when (s!.Length > 0)
        {
            return "filter";
        }
        catch (Exception)
        {
            return "next";
        }
    }

    public static string TryFinallyReturn()
    {
        var r = "";
        try
        {
            r += Inner1(ref r);
        }
        catch (Exception e)
        {
            r += "c:" + e.Message;
        }
        return r;
    }

    static string Inner1(ref string log)
    {
        try
        {
            throw new AppError("x");
        }
        finally { }
    }

    public static string StaticRo() => RoHolder.Name + RoHolder.Count;
}

public static class RoHolder
{
    public static readonly string Name;
    public static readonly int Count = 2;

    static RoHolder()
    {
        Name = "ro";
    }
}
