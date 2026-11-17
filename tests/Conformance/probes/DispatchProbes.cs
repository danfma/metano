namespace Conformance;

public class Shape { }

public class Square : Shape { }

public class BigSquare : Square { }

public interface INamed { }

public class Tagged : INamed { }

public class Disp
{
    public string M(object o) => "object";

    public string M(Shape s) => "shape";

    public string M(Square s) => "square";

    public string M(string s) => "string";

    public string M(int i) => "int";

    public string N(Shape s) => "shape";

    public string N(BigSquare s) => "big";

    public string N(Square s) => "square";

    public string O(object o) => "object";

    public string O(INamed n) => "named";

    public string O(double d) => "double";

    public string O(bool b) => "bool";

    public string P(object? o) => "object";

    public string P(string? s) => "string";

    public static string S(IEnumerable<int> xs) => "enumerable";

    public static string S(int[] xs) => "array";

    public static string S(List<int> xs) => "list";

    public static string S(object o) => "object";
}

public class Money2 : IEquatable<Money2>
{
    public int Cents;

    public Money2(int c)
    {
        Cents = c;
    }

    public bool Equals(Money2? other) => other != null && other.Cents == Cents;

    public override bool Equals(object? obj) => obj is Money2 m && Equals(m);

    public override int GetHashCode() => Cents;

    public override string ToString() => "M" + Cents;

    public string ToString(string format) => format + Cents;
}

public static class DispatchProbes
{
    public static string Direct()
    {
        var d = new Disp();
        return d.M(new object())
            + d.M(new Shape())
            + d.M(new Square())
            + d.M(new BigSquare())
            + d.M("s")
            + d.M(1);
    }

    public static string StaticTyped()
    {
        var d = new Disp();
        Shape s = new Square();
        object o = new Square();
        object os = "s";
        object oi = 1;
        return d.M(s) + d.M(o) + d.M(os) + d.M(oi);
    }

    public static string Order()
    {
        var d = new Disp();
        Shape s = new BigSquare();
        Square q = new BigSquare();
        return d.N(new Shape()) + d.N(new Square()) + d.N(new BigSquare()) + d.N(s) + d.N(q);
    }

    public static string ObjectArm()
    {
        var d = new Disp();
        object t = new Tagged();
        return d.O(new Tagged())
            + d.O(t)
            + d.O(1.5)
            + d.O(true)
            + d.O("str")
            + d.O(new Shape())
            + d.O(7)
            + d.O(new[] { 1 })
            + d.O(1.5m)
            + d.O(5L)
            + d.O('c');
    }

    public static string Nulls()
    {
        var d = new Disp();
        string? s = null;
        object? o = null;
        return d.P(s) + d.P(o) + d.P(null) + d.P("x") + d.P(3);
    }

    public static string StaticColl()
    {
        IEnumerable<int> e = new List<int>();
        object o = new[] { 1 };
        return Disp.S(new[] { 1 })
            + Disp.S(new List<int>())
            + Disp.S(e)
            + Disp.S(o)
            + Disp.S(new HashSet<int>())
            + Disp.S("x");
    }

    public static string EqualsPair()
    {
        var a = new Money2(1);
        var b = new Money2(1);
        object ob = b;
        return (a.Equals(b) ? "T" : "F")
            + (a.Equals(ob) ? "T" : "F")
            + (a.Equals("x") ? "T" : "F")
            + (a.Equals(null) ? "T" : "F")
            + (a.Equals((object?)null) ? "T" : "F")
            + (object.Equals(a, b) ? "T" : "F")
            + (new List<Money2> { a }.Contains(b) ? "T" : "F");
    }

    public static string ToStringPair()
    {
        var a = new Money2(1);
        object o = a;
        return a.ToString() + a.ToString("X") + o.ToString() + $"{a}" + ("" + a);
    }

    public static string RecordWithEquals() =>
        (new Box<Money2>(new Money2(1)) == new Box<Money2>(new Money2(1)) ? "T" : "F");

    public static string BigInt()
    {
        var b = new System.Numerics.BigInteger(5);
        var c = new System.Numerics.BigInteger(5L);
        var d = new System.Numerics.BigInteger(2.9);
        return (b + c).ToString() + "|" + d;
    }
}
