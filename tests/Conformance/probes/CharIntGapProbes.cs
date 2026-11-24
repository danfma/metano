namespace Conformance;

// #695: the char, int-wrap and user-conversion gaps left by wave 2. Each row is a
// yardstick of one item (fixed, or already fixed before wave 7).

public enum GapEnum
{
    A = 65,
    B = 66,
}

public readonly struct GapCharInt
{
    public readonly int V;

    public GapCharInt(int v) => V = v;

    public static implicit operator GapCharInt(char c) => new GapCharInt(c * 2);

    public override string ToString() => "F" + V;
}

public readonly struct GapByte
{
    public readonly int V;

    public GapByte(int v) => V = v;

    public static explicit operator byte(GapByte p) => unchecked((byte)p.V);
}

public readonly struct GapInt
{
    public readonly int V;

    public GapInt(int v) => V = v;

    public static implicit operator GapInt(int c) => new GapInt(c * 3);

    public override string ToString() => "I" + V;
}

public readonly struct GapDec
{
    public readonly decimal V;

    public GapDec(decimal v) => V = v;

    public static explicit operator decimal(GapDec x) => x.V;
}

public readonly struct GapDbl
{
    public readonly double V;

    public GapDbl(double v) => V = v;

    public static explicit operator double(GapDbl x) => x.V;
}

public readonly struct GapUInt
{
    public readonly uint V;

    public GapUInt(uint v) => V = v;

    public static explicit operator GapUInt(uint v) => new GapUInt(v);
}

public static class CharIntGapExt
{
    extension(CharIntGapBox b)
    {
        public decimal Amount
        {
            get => b.M;
            set => b.M = value;
        }
    }
}

public class CharIntGapCounter
{
    public int Count = int.MaxValue;

    public void Bump() => Count++;
}

public class CharIntGapOuter
{
    public enum Inner
    {
        A,
        B,
    }
}

public class CharIntGapBox
{
    public decimal M;
}

public static class CharIntGapProbes
{
    static string M(char c) => "char" + c;

    static string M(string s) => "str" + s;

    public static string NullableCharInc()
    {
        char? c = 'a';
        c++;
        ++c;
        return "" + c;
    }

    public static string CharFromDecimal()
    {
        decimal m = 66.7m;
        return "" + (char)m;
    }

    public static string EnumChar()
    {
        GapEnum e = GapEnum.A;
        char c = (char)e;
        GapEnum back = (GapEnum)'B';
        return c + "|" + back;
    }

    public static string CharMax()
    {
        int max = char.MaxValue;
        int min = char.MinValue;
        return max + "|" + min;
    }

    public static string IntArrayOfChars()
    {
        char c = 'a';
        int[] a = { c, 'b' };
        return string.Join(",", a);
    }

    public static string StringFromChars()
    {
        var cs = new[] { 'h', 'i' };
        return new string(cs) + "|" + new string('x', 3);
    }

    public static string CharStatics()
    {
        char c = '5';
        return char.IsDigit(c)
            + "|"
            + char.IsDigit('a')
            + "|"
            + 'a'.CompareTo('b')
            + "|"
            + 'b'.CompareTo('a');
    }

    public static string CharOverload()
    {
        char c = 'a';
        return M(c) + "|" + M("a");
    }

    public static string ForeachSurrogates()
    {
        string s = "a😀";
        int n = 0;
        string codes = "";
        foreach (var ch in s)
        {
            n++;
            codes += (int)ch + ",";
        }
        return n + "|" + codes + "|" + s.Count() + "|" + s.Select(x => (int)x).Last();
    }

    public static string LiftedUserConv()
    {
        char? nc = 'a';
        char? nn = null;
        GapCharInt? f = (GapCharInt?)nc;
        GapCharInt? g = (GapCharInt?)nn;
        return f + "|" + (g == null);
    }

    public static string WidenedUserConv()
    {
        var p = new GapByte(300);
        int a = (byte)p;
        int b = (byte)p + 1;
        long d = (byte)p;
        int s = 0;
        s += (byte)p;
        return a + "|" + b + "|" + d + "|" + s;
    }

    static string Try(Func<string> run)
    {
        try
        {
            return run();
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    static int TakeInt(int x) => x;

    static T Twice<T>(T x)
        where T : System.Numerics.INumber<T> => x + x;

    public static string NullableCharCompound()
    {
        char? c = 'a';
        c += (char)2;
        char? n = null;
        n += (char)1;
        return c + "|" + (n == null);
    }

    public static string LiftedUserConvViaInt()
    {
        char? nc = 'a';
        GapInt? f = (GapInt?)nc;
        return "" + f;
    }

    public static string DictRmw()
    {
        var d = new Dictionary<string, double> { ["a"] = 7.5 };
        d["a"] += 1.5;
        d["a"] %= 4;
        var i = new Dictionary<string, int> { ["a"] = 12 };
        i["a"] |= 1;
        i["a"] &= 7;
        i["a"] ^= 3;
        i["a"] >>= 1;
        var e = new Dictionary<string, GapEnum> { ["a"] = GapEnum.A };
        e["a"]++;
        return d["a"] + "|" + i["a"] + "|" + e["a"];
    }

    public static string VoidExprBody()
    {
        int n = int.MaxValue;
        Action step = () => n++;
        step();
        var box = new CharIntGapCounter();
        box.Bump();
        return n + "|" + box.Count;
    }

    public static string FloatCast()
    {
        double d = 0.1;
        float f = (float)d;
        return "" + (f == 0.1f);
    }

    public static string DecimalExtCompound()
    {
        var b = new CharIntGapBox { M = 1.5m };
        b.Amount += 2m;
        b.Amount--;
        return "" + b.M;
    }

    public static string RemainderEdges()
    {
        int a = 7;
        int z = 0;
        int neg = -1;
        uint u = 7;
        uint uz = 0;
        return Try(() => "" + (a % z))
            + "|"
            + (a % neg)
            + "|"
            + Try(() => "" + (u % uz))
            + "|"
            + (a % 3)
            + "|"
            + (-7 % 2);
    }

    public static string CharFromDecimalEdges()
    {
        decimal big = 70000m;
        decimal neg = -1.5m;
        return Try(() => "" + (int)(char)big) + "|" + (int)(char)neg;
    }

    public static string ConvTruncatingStep()
    {
        double d = 3.7;
        return "" + ((GapUInt)d).V;
    }

    public static string IntMinEdge()
    {
        int m = int.MinValue;
        int n = -1;
        return Try(() => "" + (m / n))
            + "|"
            + Try(() => "" + (m % n))
            + "|"
            + Try(() => "" + Math.Abs(m));
    }

    public static string ConvArg()
    {
        var p = new GapByte(300);
        GapByte? np = p;
        GapByte? none = null;
        byte? nb = (byte?)np;
        byte? nn = (byte?)none;
        long l = (short)(byte)p;
        return TakeInt((byte)p) + "|" + nb + "|" + (nn == null) + "|" + l;
    }

    public static string ConvLiftedChecked()
    {
        GapByte? p = new GapByte(300);
        return Try(() => "" + checked((byte?)p));
    }

    public static string ConvNumericStep()
    {
        int a = (int)new GapDec(-7.9m);
        float f = (float)new GapDbl(0.1);
        return a + "|" + (f == 0.1f) + "|" + ((double)f == 0.1);
    }

    public static string ConvCheckedStep()
    {
        long big = 5000000000L;
        return Try(() => "" + checked((GapUInt)big).V) + "|" + unchecked((GapUInt)big).V;
    }

    public static string DecimalFromFloat()
    {
        double d = 0.1 + 0.2;
        float f = 1.1f;
        GapEnum e = GapEnum.B;
        return (decimal)d + "|" + (decimal)f + "|" + (decimal)e + "|" + (decimal)(1.0 / 3.0);
    }

    public static string DecimalExtInc()
    {
        var b = new CharIntGapBox { M = 1.5m };
        b.Amount++;
        var dd = new Dictionary<string, decimal> { ["a"] = 2m };
        dd["a"] += 1.25m;
        dd["a"] *= 3m;
        decimal? none = null;
        return b.M + "|" + dd["a"] + "|" + (none == null);
    }

    static void AddRef(ref decimal x) => x += 1m;

    public static string RefDecimal()
    {
        decimal x = 1m;
        AddRef(ref x);
        return "" + x;
    }

    public static string TupleAndCollConv()
    {
        var p = new GapByte(300);
        (int, long) t = ((byte)p, (byte)p);
        var list = new List<int>();
        list.Add((byte)p);
        var xs = new List<GapByte> { p };
        int sum = 0;
        foreach (byte b in xs.Select(x => (byte)x))
            sum += b;
        return t.Item1 + "|" + t.Item2 + "|" + list[0] + "|" + sum;
    }

    public static string GenericMath()
    {
        return Twice(3) + "|" + Twice(1.5) + "|" + Twice(int.MaxValue);
    }

    public static string NestedEnum()
    {
        var e = CharIntGapOuter.Inner.A;
        e++;
        var d = new Dictionary<string, CharIntGapOuter.Inner> { ["k"] = CharIntGapOuter.Inner.A };
        d["k"]++;
        return e + "|" + (int)e + "|" + $"{d["k"]}" + "|" + default(CharIntGapOuter.Inner);
    }
}
