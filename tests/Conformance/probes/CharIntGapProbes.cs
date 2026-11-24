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
        return (decimal)d + "|" + (decimal)f + "|" + (decimal)e + "|" + (decimal)0.1875;
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

    // Round 1 (#695): float arithmetic rounds to single precision as a
    // `(float)` cast does, a string walks its code units without a copy, a
    // `(decimal)double` ties to even, and an extension `++` yields its value.

    public static string FloatMix()
    {
        float a = 0.1f;
        float c = a * 3f;
        float b = (float)(a * 3.0);
        double dd = 0.7;
        float x = (float)dd;
        float y = x + 0f;
        return (c == b) + "|" + (x == y) + "|" + (x == 0.7f);
    }

    public static string FloatChain()
    {
        float a = 0.1f,
            b = 0.2f,
            c = 0.3f;
        float r = a * b + a / c - b;
        double wide = a * 3.0;
        return ((double)r).ToString("R")
            + "|"
            + (a + b == c)
            + "|"
            + ((double)(a + b)).ToString("R")
            + "|"
            + wide.ToString("R");
    }

    public static string FloatCompound()
    {
        float f = 0.1f;
        f += 0.2f;
        f *= 3f;
        f /= 7f;
        f -= 0.01f;
        float m = 5.5f;
        m %= 2f;
        float[] arr = { 0.1f };
        arr[0] += 0.2f;
        var dict = new Dictionary<string, float> { ["k"] = 0.1f };
        dict["k"] += 0.2f;
        return ((double)f).ToString("R")
            + "|"
            + m
            + "|"
            + ((double)arr[0]).ToString("R")
            + "|"
            + ((double)dict["k"]).ToString("R");
    }

    static float? NullableSingle(bool some) => some ? 0.1f : null;

    public static string FloatIncrement()
    {
        float f = 16777216f;
        f++;
        float g = 0.1f;
        float old = g++;
        float pre = ++g;
        float? n = NullableSingle(true);
        n++;
        float? none = NullableSingle(false);
        none++;
        return ((double)f).ToString("R")
            + "|"
            + ((double)old).ToString("R")
            + "|"
            + ((double)pre).ToString("R")
            + "|"
            + ((double)n!.Value).ToString("R")
            + "|"
            + (none == null);
    }

    public static string FloatFromInt()
    {
        int i = 16777217;
        uint u = 4294967295u;
        float f = i;
        float g = (float)u;
        return ((double)f).ToString("R") + "|" + ((double)g).ToString("R") + "|" + (f == 16777216f);
    }

    public static string FloatLinq()
    {
        float[] xs = { 0.1f, 0.2f, 0.3f };
        float sum = xs.Sum();
        float avg = xs.Average();
        return ((double)sum).ToString("R") + "|" + ((double)avg).ToString("R");
    }

    public static string DecimalTies()
    {
        double a = 1000000000000005.0,
            b = 1000000000000025.0,
            t = 1e-30,
            u = 1.2345678901234567e-20,
            h = 5e-29;
        float f = 1.25e-7f;
        return (decimal)a
            + "|"
            + (decimal)b
            + "|"
            + (decimal)t
            + "|"
            + (decimal)u
            + "|"
            + (decimal)h
            + "|"
            + (decimal)f
            + "|"
            + (decimal)(-0.0);
    }

    public static string DecimalExtPostfix()
    {
        var b = new CharIntGapBox { M = 1.5m };
        var old = b.Amount++;
        var neu = ++b.Amount;
        var down = b.Amount--;
        return old + "|" + neu + "|" + down + "|" + b.M;
    }

    public static string StringWalkCopy()
    {
        string s = "a\uD83D\uDE00b";
        var sb = "";
        foreach (var ch in s)
        {
            s = "changed";
            sb += (int)ch + ",";
        }
        return sb
            + "|"
            + "xy\uD83D".First()
            + "|"
            + "\uDE00z".Any()
            + "|"
            + new string("a\uD83D\uDE00".Take(2).ToArray()).Length;
    }

    // Round 2 (#695): every source of a float value holds single precision.

    public static string FloatParse()
    {
        float p = float.Parse("0.1", System.Globalization.CultureInfo.InvariantCulture);
        float q = float.Parse("16777217");
        float.TryParse("0.7", out var r);
        bool bad = float.TryParse("x", out var z);
        return (p * 1f == p)
            + "|"
            + ((double)p).ToString("R")
            + "|"
            + ((double)q).ToString("R")
            + "|"
            + ((double)r).ToString("R")
            + "|"
            + (r + 0f == r)
            + "|"
            + bad
            + "|"
            + z
            + "|"
            + (p.ToString() == "0.1");
    }

    public static string FloatOtherSources()
    {
        long l = 16777217L;
        decimal m = 0.1m;
        float a = (float)l;
        float b = (float)m;
        float c = System.Convert.ToSingle(l);
        return (a * 1f == a)
            + "|"
            + ((double)a).ToString("R")
            + "|"
            + (b * 1f == b)
            + "|"
            + ((double)b).ToString("R")
            + "|"
            + ((double)c).ToString("R");
    }
}
