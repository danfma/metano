using Metano.Annotations;

namespace Lib;

public record LTol(double V)
{
    public bool Equals(LTol o, double tol) => Math.Abs(o.V - V) <= tol;

    public LTol With(double v) => new LTol(v + 100);

    public int HashCode(int seed) => seed;
}

public struct LS
{
    public int X;

    public LS(int x)
    {
        X = x;
    }

    public bool Equals(LS o, int slack) => Math.Abs(o.X - X) <= slack;
}

public record LPriv(double V)
{
    private bool Equals(LPriv o, double tol) => Math.Abs(o.V - V) <= tol;

    public bool Near(LPriv o) => Equals(o, 0.1);

    private int HashCode(int seed) => seed * 2;

    public int Seeded() => HashCode(4);
}

public record LBase(int A)
{
    public bool Equals(LBase o, int slack) => Math.Abs(o.A - A) <= slack;

    public virtual int HashCode(int seed) => A + seed;
}

public interface ILTol
{
    bool Equals(ILTol o, double tol);
}

public record LPlain(int A);

public record LNamed(double V)
{
    [Name("equals")]
    public bool Same(LNamed o, double tol) => Math.Abs(o.V - V) <= tol;

    [Name("hashCode")]
    public int Seeded(int s) => s;

    [Name("with")]
    public LNamed Repl(double v) => new LNamed(v + 1);
}

public struct LSEq : IEquatable<LSEq>
{
    public int X;
    public int Noise;

    public LSEq(int x, int n)
    {
        X = x;
        Noise = n;
    }

    public bool Equals(LSEq o) => o.X == X;

    public override int GetHashCode() => X;
}

public struct LSEqJoin : IEquatable<LSEqJoin>
{
    public int X;
    public int Noise;

    public LSEqJoin(int x, int n)
    {
        X = x;
        Noise = n;
    }

    public bool Equals(LSEqJoin o) => o.X == X;

    public bool Equals(LSEqJoin o, int slack) => Math.Abs(o.X - X) <= slack;

    public override int GetHashCode() => X;
}

public struct LSExpl : IEquatable<LSExpl>
{
    public int X;
    public int Noise;

    public LSExpl(int x, int n)
    {
        X = x;
        Noise = n;
    }

    bool IEquatable<LSExpl>.Equals(LSExpl o) => o.X == X;

    public override int GetHashCode() => X;
}

public record LOwn(int A, int N)
{
    public virtual bool Equals(LOwn? o) => o is not null && o.A == A;

    public override int GetHashCode() => A;

    public bool Equals(LOwn o, int slack) => Math.Abs(o.A - A) <= slack;
}

public record LZero(int V)
{
    public int HashCode() => 5;

    public LZero With() => new LZero(V + 1);
}
