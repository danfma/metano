namespace Conformance;

public class RefClass
{
    public int V;

    public RefClass(int v)
    {
        V = v;
    }
}

public class EqClass
{
    public int V;

    public EqClass(int v)
    {
        V = v;
    }

    public override bool Equals(object? obj) => obj is EqClass o && o.V == V;

    public override int GetHashCode() => V;
}

public interface IShape { }

public record Circle(int R) : IShape;

public class Blob : IShape { }

public struct Pt
{
    public int X;
    public int Y;

    public Pt(int x, int y)
    {
        X = x;
        Y = y;
    }
}

public record struct RPt(int X, int Y);

public enum Color
{
    Red,
    Green,
}

public record Box<T>(T Value);

public record ObjBox(object? Value);

public record ShapeBox(IShape? Shape);

public record RefBox(RefClass? Ref);

public record EqBox(EqClass? Eq);

public record DblBox(double D, float F);

public record NDblBox(double? D);

public record ArrBox(int[] Xs);

public record ListBox(List<int> Xs);

public record Inner(string S);

public record Outer(Inner In, Pt P, RPt R, (int, string) T);

public record Misc(
    decimal M,
    Guid G,
    DateTime When,
    long L,
    Color C,
    string? S,
    int? N,
    TimeSpan Span,
    DateOnly Day
);

public record FnBox(Func<int, int> F);

public record ExBox(Exception? E);

public record StrBox(string S);

public static class EqualityProbes
{
    static string B(bool b) => b ? "T" : "F";

    public static string GenericInt() =>
        B(new Box<int>(1) == new Box<int>(1)) + B(new Box<int>(1) == new Box<int>(2));

    public static string GenericString() =>
        B(new Box<string>("a") == new Box<string>("a"))
        + B(new Box<string?>(null) == new Box<string?>(null))
        + B(new Box<string?>(null) == new Box<string?>("a"));

    public static string GenericDouble() =>
        B(new Box<double>(double.NaN) == new Box<double>(double.NaN))
        + B(new Box<double>(0.0) == new Box<double>(-0.0));

    public static string GenericRecord() =>
        B(new Box<Inner>(new Inner("a")) == new Box<Inner>(new Inner("a")));

    public static string GenericRefClass()
    {
        var r = new RefClass(1);
        return B(new Box<RefClass>(new RefClass(1)) == new Box<RefClass>(new RefClass(1)))
            + B(new Box<RefClass>(r) == new Box<RefClass>(r));
    }

    public static string GenericEqClass() =>
        B(new Box<EqClass>(new EqClass(1)) == new Box<EqClass>(new EqClass(1)))
        + B(new Box<EqClass>(new EqClass(1)) == new Box<EqClass>(new EqClass(2)));

    public static string GenericStruct() =>
        B(new Box<Pt>(new Pt(1, 2)) == new Box<Pt>(new Pt(1, 2)))
        + B(new Box<Pt>(new Pt(1, 2)) == new Box<Pt>(new Pt(1, 3)));

    public static string GenericArray()
    {
        var a = new[] { 1 };
        return B(new Box<int[]>(new[] { 1 }) == new Box<int[]>(new[] { 1 }))
            + B(new Box<int[]>(a) == new Box<int[]>(a));
    }

    public static string GenericList()
    {
        var a = new List<int> { 1 };
        return B(new Box<List<int>>(new List<int> { 1 }) == new Box<List<int>>(new List<int> { 1 }))
            + B(new Box<List<int>>(a) == new Box<List<int>>(a));
    }

    public static string GenericDecimal() =>
        B(new Box<decimal>(1.0m) == new Box<decimal>(1.00m))
        + B(new Box<decimal>(1m) == new Box<decimal>(2m));

    public static string GenericLong() => B(new Box<long>(5L) == new Box<long>(5L));

    public static string GenericGuid()
    {
        var g = Guid.Parse("11111111-2222-3333-4444-555555555555");
        return B(
            new Box<Guid>(g) == new Box<Guid>(Guid.Parse("11111111-2222-3333-4444-555555555555"))
        );
    }

    public static string GenericDate() =>
        B(
            new Box<DateTime>(new DateTime(2020, 1, 2))
                == new Box<DateTime>(new DateTime(2020, 1, 2))
        );

    public static string GenericTuple() =>
        B(new Box<(int, string)>((1, "a")) == new Box<(int, string)>((1, "a")));

    public static string GenericEnum() =>
        B(new Box<Color>(Color.Red) == new Box<Color>(Color.Red))
        + B(new Box<Color>(Color.Red) == new Box<Color>(Color.Green));

    public static string GenericNullableInt() =>
        B(new Box<int?>(null) == new Box<int?>(null))
        + B(new Box<int?>(1) == new Box<int?>(1))
        + B(new Box<int?>(1) == new Box<int?>(null));

    public static string GenericBool() => B(new Box<bool>(true) == new Box<bool>(true));

    public static string ObjectBoxed() =>
        B(new ObjBox(1) == new ObjBox(1))
        + B(new ObjBox("a") == new ObjBox("a"))
        + B(new ObjBox(null) == new ObjBox(null))
        + B(new ObjBox(1) == new ObjBox(1L))
        + B(new ObjBox(1) == new ObjBox(1.0))
        + B(new ObjBox(new Inner("x")) == new ObjBox(new Inner("x")))
        + B(new ObjBox(new RefClass(1)) == new ObjBox(new RefClass(1)))
        + B(new ObjBox(double.NaN) == new ObjBox(double.NaN));

    public static string ObjectArrays()
    {
        return B(new ObjBox(new[] { 1 }) == new ObjBox(new[] { 1 }))
            + B(new ObjBox((1, 2)) == new ObjBox((1, 2)))
            + B(new ObjBox(new Pt(1, 2)) == new ObjBox(new Pt(1, 2)))
            + B(new ObjBox(1.5m) == new ObjBox(1.50m));
    }

    public static string Interface()
    {
        var b = new Blob();
        return B(new ShapeBox(new Circle(1)) == new ShapeBox(new Circle(1)))
            + B(new ShapeBox(new Blob()) == new ShapeBox(new Blob()))
            + B(new ShapeBox(b) == new ShapeBox(b))
            + B(new ShapeBox(null) == new ShapeBox(null));
    }

    public static string ClassRef()
    {
        var r = new RefClass(1);
        return B(new RefBox(new RefClass(1)) == new RefBox(new RefClass(1)))
            + B(new RefBox(r) == new RefBox(r))
            + B(new RefBox(null) == new RefBox(null))
            + B(new RefBox(r) == new RefBox(null));
    }

    public static string ClassEq() =>
        B(new EqBox(new EqClass(1)) == new EqBox(new EqClass(1)))
        + B(new EqBox(new EqClass(1)) == new EqBox(new EqClass(2)))
        + B(new EqBox(null) == new EqBox(new EqClass(2)));

    public static string Doubles() =>
        B(new DblBox(double.NaN, float.NaN) == new DblBox(double.NaN, float.NaN))
        + B(new DblBox(0.0, 0f) == new DblBox(-0.0, -0f))
        + B(new DblBox(1.5, 2.5f) == new DblBox(1.5, 2.5f))
        + B(new DblBox(1.5, 2.5f) == new DblBox(1.5, 2f));

    public static string NullableDouble() =>
        B(new NDblBox(null) == new NDblBox(null))
        + B(new NDblBox(double.NaN) == new NDblBox(double.NaN))
        + B(new NDblBox(1) == new NDblBox(null));

    public static string Arrays()
    {
        var a = new[] { 1 };
        return B(new ArrBox(new[] { 1 }) == new ArrBox(new[] { 1 }))
            + B(new ArrBox(a) == new ArrBox(a));
    }

    public static string Lists()
    {
        var a = new List<int> { 1 };
        return B(new ListBox(new List<int> { 1 }) == new ListBox(new List<int> { 1 }))
            + B(new ListBox(a) == new ListBox(a));
    }

    public static string Nested() =>
        B(
            new Outer(new Inner("a"), new Pt(1, 2), new RPt(3, 4), (1, "x"))
                == new Outer(new Inner("a"), new Pt(1, 2), new RPt(3, 4), (1, "x"))
        )
        + B(
            new Outer(new Inner("a"), new Pt(1, 2), new RPt(3, 4), (1, "x"))
                == new Outer(new Inner("a"), new Pt(1, 2), new RPt(3, 5), (1, "x"))
        )
        + B(
            new Outer(new Inner("a"), new Pt(1, 2), new RPt(3, 4), (1, "x"))
                == new Outer(new Inner("a"), new Pt(1, 2), new RPt(3, 4), (1, "y"))
        );

    public static string MiscAll()
    {
        var g = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var a = new Misc(
            1.10m,
            g,
            new DateTime(2020, 1, 2),
            5L,
            Color.Green,
            null,
            null,
            TimeSpan.FromSeconds(3),
            new DateOnly(2020, 1, 2)
        );
        var b = new Misc(
            1.1m,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            new DateTime(2020, 1, 2),
            5L,
            Color.Green,
            null,
            null,
            TimeSpan.FromSeconds(3),
            new DateOnly(2020, 1, 2)
        );
        return B(a == b)
            + B(a.Equals(b))
            + B(a != b)
            + B(a == b with { N = 1 })
            + B(a == b with { S = "" })
            + B(a.GetHashCode() == b.GetHashCode());
    }

    public static string Delegates()
    {
        Func<int, int> f = x => x;
        return B(new FnBox(f) == new FnBox(f)) + B(new FnBox(x => x) == new FnBox(x => x));
    }

    public static string Exceptions()
    {
        var e = new Exception("a");
        return B(new ExBox(e) == new ExBox(e))
            + B(new ExBox(new Exception("a")) == new ExBox(new Exception("a")))
            + B(new ExBox(null) == new ExBox(null));
    }

    public static string Strings() =>
        B(new StrBox("a") == new StrBox("a"))
        + B(new StrBox("a") == new StrBox("b"))
        + B(new StrBox("a").Equals((object)new StrBox("a")))
        + B(new StrBox("a").Equals(null));

    public static string StructEq() =>
        B(new RPt(1, 2) == new RPt(1, 2))
        + B(new RPt(1, 2).Equals(new RPt(1, 3)))
        + B(new Pt(1, 2).Equals(new Pt(1, 2)));

    public static string HashCodes() =>
        B(new Box<double>(double.NaN).GetHashCode() == new Box<double>(double.NaN).GetHashCode())
        + B(new DblBox(1, 2).GetHashCode() == new DblBox(1, 2).GetHashCode())
        + B(new ObjBox(1).GetHashCode() == new ObjBox(1).GetHashCode())
        + B(new EqBox(new EqClass(1)).GetHashCode() == new EqBox(new EqClass(1)).GetHashCode());

    public static string GenericDerived()
    {
        Box<IShape> a = new Box<IShape>(new Circle(1));
        return B(a == new Box<IShape>(new Circle(1)))
            + B(new Box<object>(1) == new Box<object>(1))
            + B(new Box<object>("a") == new Box<object>("a"));
    }
}
