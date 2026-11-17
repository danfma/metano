using Metano.Annotations;

namespace Conformance;

public readonly record struct OpPoint(int X, int Y)
{
    public static OpPoint operator -(OpPoint a) => new(-a.X, -a.Y);

    public static OpPoint operator -(OpPoint a, OpPoint b) => new(a.X - b.X, a.Y - b.Y);
}

public class OpMeters
{
    public int V;

    public OpMeters(int v)
    {
        V = v;
    }

    public static OpMeters operator +(int a, OpMeters m) => new(a + m.V);

    public static OpMeters operator ++(OpMeters m) => new(m.V + 1);
}

public class OpMoney
{
    public int Cents;

    public OpMoney(int cents)
    {
        Cents = cents;
    }

    public static implicit operator OpMoney(int c) => new(c);

    public static explicit operator int(OpMoney m) => m.Cents;

    public static explicit operator long(OpMoney m) => m.Cents * 1000L;

    public static implicit operator string(OpMoney m) => "$" + m.Cents;

    public override string ToString() => "M" + Cents;
}

public class OpPinned
{
    public int C;

    public OpPinned(int c)
    {
        C = c;
    }

    [Name("toInt")]
    public static explicit operator int(OpPinned p) => p.C;

    [Name("toLong")]
    public static explicit operator long(OpPinned p) => p.C * 2L;
}

public class OpBox<T>
{
    public T Item;

    public int Count;

    public OpBox(T item, int count)
    {
        Item = item;
        Count = count;
    }

    public static OpBox<T> operator +(OpBox<T> b, int n) => new(b.Item, b.Count + n);

    public static implicit operator T(OpBox<T> b) => b.Item;
}

[Branded]
public readonly record struct OpQty(int Value)
{
    public static OpQty operator +(OpQty a, OpQty b) => new(a.Value + b.Value + 1000);

    public static implicit operator OpQty(int v) => new(v * 2);

    public static explicit operator int(OpQty q) => q.Value + 7;
}

/// <summary>
/// User-defined operators and conversions (baseline FR-077–FR-079, #599,
/// #600). Slice G3b changes only their declarations, which C# cannot call by
/// name; these rows record the use sites G3c lowers.
/// </summary>
public static class OperatorProbes
{
    public static string UnaryAndBinaryMinus()
    {
        var a = -new OpPoint(1, 2);
        var b = new OpPoint(5, 5) - new OpPoint(1, 2);

        return a.X + "," + a.Y + "|" + b.X + "," + b.Y;
    }

    public static string RightOperand() => (5 + new OpMeters(2)).V.ToString();

    public static string Increment()
    {
        var m = new OpMeters(1);
        m++;

        return m.V.ToString();
    }

    public static string Conversions()
    {
        OpMoney m = 7;
        string s = m;

        return m + "|" + (int)m + "|" + (long)m + "|" + s;
    }

    public static string PinnedConversions()
    {
        var p = new OpPinned(4);

        return (int)p + "|" + (long)p;
    }

    public static string GenericOperatorAndConversion()
    {
        var b = new OpBox<string>("s", 1) + 2;
        string item = b;

        return b.Count + item;
    }

    public static string BrandedOperatorAndConversions()
    {
        OpQty q = 5;
        var sum = q + new OpQty(1);

        return q.Value + "|" + sum.Value + "|" + (int)new OpQty(1);
    }
}
