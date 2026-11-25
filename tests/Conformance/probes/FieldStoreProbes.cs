using Metano.Annotations;

namespace Conformance;

// Wave 10 group A: instance fields as declarations with one constructor store (#795, #787 N4/N11).

[ExportedAsModule]
public static class FsHelpers
{
    static int _next;

    public static int Compute() => 7;

    public static int Next() => ++_next;

    public static void Reset() => _next = 0;
}

public class FsSequence
{
    public int A = FsHelpers.Next();
    public int B;
    public int C = FsHelpers.Next();

    public FsSequence() => B = FsHelpers.Next();
}

public class FsBase
{
    public int Base = 10;
}

public class FsDerived : FsBase
{
    public int Own = FsHelpers.Next();
    public string? Name;
    public int Late;

    public FsDerived(int late)
    {
        Late = late;
    }
}

public class FsAssignedFirst
{
    private Func<int>? _compute;
    private int _version = 3;

    public FsAssignedFirst(Func<int> compute)
    {
        _compute = compute;
        _version = _compute() + _version;
    }

    public int Version => _version;
}

public class FsChained
{
    private readonly List<int> _items = new();
    private int _id = FsHelpers.Next();
    public string Name = "";

    public FsChained()
        : this("d") => _items.Add(1);

    public FsChained(string name)
    {
        Name = name;
        _items.Add(_id);
    }

    public string Show() => $"{Name}:{_id}:{_items.Count}";
}

public class FsShadow
{
    private int _x = FsHelpers.Compute();

    public FsShadow(int compute)
    {
        Y = compute;
    }

    public int Y { get; }

    public int X => _x;
}

public record FsRecord(string Name, int Age)
{
    public int Extra { get; init; } = 3;
    public string? Tag { get; set; }
}

public record FsDerivedRecord(string Name, int Age, bool Flag) : FsRecord(Name, Age);

public struct FsPoint
{
    public static int Created;
    public int X = 5;
    public int Y;

    static FsPoint() => Created = 100;

    public FsPoint(int y) => Y = y;
}

public class FsError : Exception
{
    public int Code = 3;

    public FsError(string message)
        : base(message) { }
}

public class FsRequired
{
    public required string Name { get; init; }
    public int Count { get; set; } = 2;
}

public class FsComputed
{
    private readonly string _name;
    private readonly Func<int>? _f;

    public FsComputed(string n, Func<int> f)
    {
        _f = f;
        _name = n;
    }

    public string N => _name + _f!();
}

[NoContainer]
public static class FsFlat
{
    public static int Twice() => 14;
}

public record FsPromotedShadow(Func<int> Twice)
{
    public int X { get; } = FsFlat.Twice();
}

public class FsCapturedShadow(Func<int> twice)
{
    public int X { get; } = FsFlat.Twice();

    public int Y => twice();
}

[ObjectArgs]
public class FsObjectArgsShadow
{
    private readonly int _x = FsFlat.Twice();

    public FsObjectArgsShadow(int twice)
    {
        Y = twice * 2;
    }

    public int Y { get; }

    public int X => _x;
}

public partial class FsPartialShadow
{
    public int Y;

    public FsPartialShadow(Func<int> twice)
    {
        Y = twice();
    }
}

public static class FsEscapeLog
{
    public static object? Last;
}

public class FsPublishingBase
{
    public FsPublishingBase()
    {
        FsEscapeLog.Last = this;
    }
}

public class FsObservedDerived : FsPublishingBase
{
    private readonly int _n;
    private readonly int _k;

    public FsObservedDerived()
    {
        _n = Peek();
        _k = 1;
    }

    private static int Peek() => ((FsObservedDerived)FsEscapeLog.Last!)._k + 10;

    public int N => _n + _k;
}

public static class FieldStoreProbes
{
    public static string InitializersRunInDeclarationOrderBeforeTheBody()
    {
        FsHelpers.Reset();
        var s = new FsSequence();

        return $"{s.A}{s.B}{s.C}";
    }

    public static string DerivedFieldsStoreAfterTheBase()
    {
        FsHelpers.Reset();
        var d = new FsDerived(9);

        return $"{d.Base}{d.Own}|{d.Name ?? "null"}|{d.Late}";
    }

    public static string FieldAssignedFirstKeepsItsValue() =>
        new FsAssignedFirst(() => 4).Version.ToString();

    public static string ChainedConstructorRunsInitializersOnce()
    {
        FsHelpers.Reset();
        var a = new FsChained();
        var b = new FsChained("x");

        return $"{a.Show()}|{b.Show()}";
    }

    public static string ParameterNamedLikeACalledFunction()
    {
        var s = new FsShadow(2);

        return $"{s.X}|{s.Y}";
    }

    public static string RecordMembersAndWith()
    {
        var r = new FsRecord("a", 1);
        var w = r with { Extra = 5, Tag = "t" };
        var d = new FsDerivedRecord("b", 2, true);

        return $"{r.Name}{r.Age}{r.Extra}{r.Tag ?? "null"}|{w.Extra}{w.Tag}|{d.Name}{d.Age}{d.Flag}{d.Extra}|{r == new FsRecord("a", 1)}";
    }

    public static string StructInitializerRunsOnlyInADeclaredConstructor()
    {
        var declared = new FsPoint(2);
        var implicitDefault = new FsPoint();

        return $"{declared.X}{declared.Y}|{implicitDefault.X}{implicitDefault.Y}|{FsPoint.Created}";
    }

    public static string ExceptionFieldInitializer()
    {
        var e = new FsError("boom");

        return $"{e.Code}|{e.Message}";
    }

    public static string RequiredMemberThroughAnObjectInitializer()
    {
        var r = new FsRequired { Name = "n" };

        return $"{r.Name}{r.Count}";
    }

    public static string ComputedPropertyNamedLikeAParameter() => new FsComputed("a", () => 1).N;

    public static string PromotedOrCapturedParameterNamedLikeACalledFunction() =>
        $"{new FsPromotedShadow(() => 1).X}|{new FsCapturedShadow(() => 2).X}|{new FsCapturedShadow(() => 2).Y}";

    public static string ObjectArgsParameterNamedLikeACalledFunction() =>
        (new FsObjectArgsShadow(twice: 3).Y + new FsObjectArgsShadow(4).X).ToString();

    public static string PartialInitializerNamedLikeAParameter()
    {
        var p = new FsPartialShadow(() => 1);

        return $"{p.X}|{p.Y}";
    }

    public static string BaseConstructorPublishesThis() => new FsObservedDerived().N.ToString();
}
