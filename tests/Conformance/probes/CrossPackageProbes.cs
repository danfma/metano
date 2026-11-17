using Lib;

namespace Conformance;

public record APriv(double V, int N) : LPriv(V);

public record ADerived(int A, int B) : LBase(A)
{
    public override int HashCode(int seed) => B + seed;
}

public record AOwn(int A, int B) : LBase(A)
{
    public bool Equals(AOwn o, string tag) => tag == "y";
}

public record AIface(double V) : ILTol
{
    public bool Equals(ILTol o, double tol) => o is AIface a && Math.Abs(a.V - V) <= tol;
}

public record APlainDer(int A, int B) : LPlain(A)
{
    public bool Equals(APlainDer o, int slack) => Math.Abs(o.B - B) <= slack;
}

public record ANamedDer(double V, int N) : LNamed(V);

public record AOwnDer(int A, int N, int M) : LOwn(A, N);

public record AHoldS(LSEq S, LSEqJoin J);

public record AHoldT<T>(T Value);

public record AHold(object Value);

public static class CrossPackageProbes
{
    private static string B(bool b) => b ? "T" : "F";

    public static string Named()
    {
        var a = new LNamed(1.0);
        var b = new LNamed(1.05);
        return B(a.Same(b, 0.1))
            + B(a == b)
            + B(a.Equals(new LNamed(1.0)))
            + a.Seeded(4)
            + a.Repl(2).V
            + (a with { V = 3 }).V
            + B(new HashSet<LNamed> { a }.Contains(new LNamed(1.0)))
            + B(a.GetHashCode() == new LNamed(1.0).GetHashCode())
            + B(new AHold(a).Equals(new AHold(new LNamed(1.0))));
    }

    public static string NamedDer()
    {
        var d = new ANamedDer(1.0, 1);
        LNamed b = d;
        return B(d.Same(new ANamedDer(1.05, 2), 0.1))
            + B(d == new ANamedDer(1.0, 1))
            + B(d == new ANamedDer(1.0, 2))
            + B(b.Equals(new LNamed(1.0)))
            + B((b with { V = 2 }) is ANamedDer)
            + d.Seeded(3)
            + d.Repl(1).V
            + B(new HashSet<LNamed> { d }.Contains(new ANamedDer(1.0, 1)));
    }

    public static string StructEq()
    {
        var a = new LSEq(1, 5);
        var b = new LSEq(1, 6);
        var j = new LSEqJoin(1, 5);
        var k = new LSEqJoin(1, 6);
        var m = new LSEqJoin(2, 5);
        var x = new LSExpl(1, 5);
        var y = new LSExpl(1, 6);
        IEquatable<LSExpl> e = x;
        return B(a.Equals(b))
            + B(new HashSet<LSEq> { a }.Contains(b))
            + B(new AHoldS(a, j) == new AHoldS(b, k))
            + B(new AHoldS(a, j) == new AHoldS(b, m))
            + new List<LSEq> { a, b }
                .Distinct()
                .Count()
            + B(j.Equals(k))
            + B(j.Equals(m, 1))
            + B(j.Equals(m, 0))
            + B(new HashSet<LSEqJoin> { j }.Contains(k))
            + B(e.Equals(y))
            + B(new HashSet<LSExpl> { x }.Contains(y))
            + B(new AHoldT<LSExpl>(x) == new AHoldT<LSExpl>(y));
    }

    public static string OwnDer()
    {
        var x = new AOwnDer(1, 5, 1);
        LOwn bx = x;
        return B(x.Equals(new AOwnDer(1, 6, 1)))
            + B(x == new AOwnDer(1, 6, 2))
            + B(bx == new AOwnDer(1, 6, 1))
            + B(x.Equals(new AOwnDer(2, 0, 0), 1))
            + B(new HashSet<LOwn> { x }.Contains(new AOwnDer(1, 9, 1)))
            + B(new HashSet<LOwn> { x }.Contains(new AOwnDer(1, 9, 2)))
            + B(new LOwn(1, 0).Equals(bx))
            + B(new LOwn(1, 5) == new LOwn(1, 6));
    }

    public static string Zero()
    {
        var z = new LZero(1);
        return z.HashCode()
            + ""
            + z.With().V
            + (z with { }).V
            + B(z.GetHashCode() == new LZero(1).GetHashCode())
            + B(new HashSet<LZero> { z }.Contains(new LZero(1)))
            + B(z == new LZero(1));
    }

    public static string Tol()
    {
        var a = new LTol(1.0);
        var b = new LTol(1.05);
        return B(a.Equals(b, 0.1))
            + B(a == b)
            + B(a.Equals(b))
            + B(a.Equals(new LTol(1.0)))
            + a.With(2.0).V
            + (a with { V = 3 }).V
            + "|"
            + a.HashCode(4)
            + B(a.GetHashCode() == new LTol(1.0).GetHashCode())
            + B(new HashSet<LTol> { a }.Contains(new LTol(1.0)))
            + B(new AHold(a).Equals(new AHold(new LTol(1.0))));
    }

    public static string Struct()
    {
        var a = new LS(1);
        var b = new LS(2);
        return B(a.Equals(b, 1))
            + B(a.Equals(b))
            + B(a.Equals(new LS(1)))
            + B(new HashSet<LS> { a }.Contains(new LS(1)))
            + B(new AHold(a).Equals(new AHold(new LS(1))));
    }

    public static string Priv()
    {
        var a = new LPriv(1.0);
        var b = new LPriv(1.05);
        return B(a.Near(b))
            + B(a == b)
            + B(a.Equals(new LPriv(1.0)))
            + a.Seeded()
            + B(a.GetHashCode() == new LPriv(1.0).GetHashCode())
            + B(new HashSet<LPriv> { a }.Contains(new LPriv(1.0)));
    }

    public static string PrivDer()
    {
        var a = new APriv(1.0, 1);
        var b = new APriv(1.05, 1);
        LPriv ba = a;
        return B(a.Near(b))
            + B(a == b)
            + B(a.Equals(new APriv(1.0, 1)))
            + B(a.Equals(new APriv(1.0, 2)))
            + B(ba.Equals(new APriv(1.0, 1)))
            + B(ba == new LPriv(1.0))
            + B(new LPriv(1.0).Equals(ba))
            + a.Seeded()
            + B(a.GetHashCode() == new APriv(1.0, 1).GetHashCode())
            + B(new HashSet<LPriv> { a }.Contains(new APriv(1.0, 1)))
            + B(new HashSet<LPriv> { a }.Contains(new APriv(1.0, 2)))
            + (a with { N = 5 }).N
            + B((ba with { V = 2 }) is APriv);
    }

    public static string Derived()
    {
        var a = new ADerived(1, 2);
        LBase b = a;
        return B(a.Equals(new ADerived(2, 9), 1))
            + B(b.Equals(new LBase(3), 1))
            + a.HashCode(10)
            + "|"
            + b.HashCode(10)
            + B(a == new ADerived(1, 2))
            + B(a == new ADerived(1, 3))
            + B(b.Equals(new ADerived(1, 2)))
            + B(b.Equals(new LBase(1)))
            + B(new LBase(1).Equals(b))
            + B(new HashSet<LBase> { a }.Contains(new ADerived(1, 2)))
            + B(a.GetHashCode() == new ADerived(1, 2).GetHashCode())
            + (a with { B = 7 }).B;
    }

    public static string Own()
    {
        var a = new AOwn(1, 2);
        LBase b = a;
        return B(a.Equals(new AOwn(9, 9), "y"))
            + B(a.Equals(new AOwn(2, 9), 1))
            + B(a.Equals(new AOwn(1, 2)))
            + B(a == new AOwn(1, 3))
            + B(b.Equals(new AOwn(1, 2)))
            + B(b == new LBase(1))
            + B(new HashSet<LBase> { a }.Contains(new AOwn(1, 2)));
    }

    public static string Iface()
    {
        var a = new AIface(1.0);
        ILTol i = a;
        return B(i.Equals(new AIface(1.05), 0.1))
            + B(a.Equals(new AIface(1.05), 0.1))
            + B(a == new AIface(1.0))
            + B(a.Equals(new AIface(1.05)));
    }

    public static string PlainDer()
    {
        var a = new APlainDer(1, 2);
        LPlain b = a;
        return B(a.Equals(new APlainDer(1, 3), 1))
            + B(a == new APlainDer(1, 2))
            + B(b.Equals(new APlainDer(1, 2)))
            + B(b.Equals(new APlainDer(1, 3)))
            + B(b == new LPlain(1))
            + B(new LPlain(1).Equals(b))
            + B(new HashSet<LPlain> { a }.Contains(new APlainDer(1, 2)))
            + ((b with { A = 3 }) is APlainDer d ? d.B + d.A : -1);
    }
}
