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
}
