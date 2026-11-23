using Metano.Annotations;

namespace Conformance;

// Wave 6 group F: classes and members (#652, #441, #404, #384, #422, #451, #436, #417).

[PlainObject]
public record CmLoopPoint(int X, int[] Items)
{
    public int Sum()
    {
        var t = 0;

        for (var i = 0; i < this.Items.Length; i++)
            t += this.X;

        foreach (var it in this.Items)
            t += it * this.X;

        switch (t)
        {
            case 0:
                t += this.X;
                break;
            default:
                t += 2 * this.X;
                break;
        }

        try
        {
            t += this.X;
        }
        finally
        {
            t += this.X;
        }

        return t;
    }
}

[PlainObject]
public record CmChained(int A, string B, int C = 7)
{
    public CmChained(int a)
        : this(a, "x") { }

    public CmChained()
        : this(2) { }
}

[PlainObject]
public record CmPair(int A, int B)
{
    public CmPair(int a)
        : this(a, a) { }
}

public class CmReserved(int of)
{
    public int Of { get; } = of;
}

public record CmReservedRecord(int of, int @new);

public record CmBase
{
    public virtual int V { get; set; }
}

public record CmDerived : CmBase
{
    public override int V { get; set; }

    public void SetBase(int value) => base.V = value;
}

public static class CmSiblings
{
    private sealed record Leaf(int X) : Root(X);

    private abstract record Root(int X)
    {
        public int Twice() => X * 2;
    }

    internal class Child : Parent { }

    internal class Parent
    {
        public int Y => 5;
    }

    public static int Run() => new Leaf(3).Twice() + new Child().Y;
}

[PlainObject]
public record CmGeneric<T>(T V, int N)
{
    public T First() => this.V;

    public string Describe(Func<T, string> show) => show(V) + "/" + N;
}

public class CmGen<T>
{
    public T Value { get; set; }

    public class Sub : CmGen<T>
    {
        public T Apply(Func<T, T> f) => f(Value);
    }

    public class Node
    {
        public T Item { get; set; }
    }
}

public class CmSeeded(int seed)
{
    public int Seed => seed;

    public CmSeeded()
        : this(1) { }
}

public static class ClassMemberProbes
{
    static string B(bool b) => b ? "T" : "F";

    static int _calls;

    static int NextCall() => ++_calls;

    public static string PlainObjectThisInStatements() =>
        new CmLoopPoint(3, [1, 2]).Sum().ToString();

    public static string PlainObjectChainedConstructor()
    {
        var one = new CmChained(1);
        var two = new CmChained();
        _calls = 0;
        var pair = new CmPair(NextCall());

        return $"{one.A}{one.B}{one.C}|{two.A}{two.B}{two.C}|{pair.A}{pair.B}|{_calls}";
    }

    public static string ReservedWordMembers()
    {
        var button = new CmReserved(4);
        var a = new CmReservedRecord(1, 2);
        var b = new CmReservedRecord(1, 2);

        return $"{button.Of}|{a.of}{a.@new}|{B(a.Equals(b))}{B(a == new CmReservedRecord(1, 3))}";
    }

    public static string RecordEqualityPerDeclaringClass()
    {
        var a = new CmDerived { V = 1 };
        var b = new CmDerived { V = 1 };
        var before = a.Equals(b);
        var hashBefore = a.GetHashCode() == b.GetHashCode();
        a.SetBase(5);

        return $"{B(before)}{B(hashBefore)}|{B(a.Equals(b))}|{a.V}";
    }

    public static string NonPublicNestedBase() => CmSiblings.Run().ToString();

    public static string GenericPlainObject()
    {
        var g = new CmGeneric<string>("a", 1);

        return g.First() + "|" + g.Describe(s => s.ToUpperInvariant());
    }

    public static string NestedGenericSubclass()
    {
        var sub = new CmGen<int>.Sub();
        sub.Value = 3;
        CmGen<int>.Sub alias = sub;
        var node = new CmGen<string>.Node { Item = "xy" };

        return $"{alias.Apply(v => v * 2)}|{node.Item.Length}";
    }

    public static string PrimaryConstructorChain() =>
        $"{new CmSeeded().Seed}|{new CmSeeded(5).Seed}";
}
