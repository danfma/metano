using Lib;

namespace Conformance;

public enum RtsColor
{
    Red,
    Green,
}

public record RtsItem(string Name, int Qty);

public record struct RtsPoint(int X, int Y);

public readonly record struct RtsReadOnlyPoint(double X, double Y);

public record RtsEmpty;

public sealed record RtsSealedEmpty;

public sealed record RtsSealedRoot(int A, string B);

public record RtsBase(string Name);

public record RtsDerived(string Name, int Level) : RtsBase(Name);

public sealed record RtsSealedDerived(string Name, double Ratio) : RtsBase(Name);

public record RtsDerivedEmpty(string Name) : RtsBase(Name);

public record RtsEmptyBase;

public record RtsAfterEmptyBase(int A) : RtsEmptyBase;

public record RtsNested(RtsItem Inner, string? Note, char Initial);

public record RtsWithCollections(string Name, List<int> Values, Dictionary<string, int> Map);

public record RtsCustom(int A)
{
    public override string ToString() => "custom:" + A;
}

public record RtsCustomBase(int A)
{
    public sealed override string ToString() => "sealed:" + A;
}

public record RtsCustomDerived(int A, int B) : RtsCustomBase(A);

public record RtsUserBase(int A)
{
    public override string ToString() => "userbase";
}

public record RtsUserDerived(int A, int B) : RtsUserBase(A);

public abstract record RtsAbstractBase(int A)
{
    public abstract override string ToString();
}

public record RtsAbstractDerived(int A, int B) : RtsAbstractBase(A);

public record RtsNumbers(float F, double D, decimal M, long L, ulong U, bool B, RtsColor E);

public record RtsNulls(string? S, int? N, RtsItem? R, RtsColor? E, double? D);

public record RtsText(string S, char C, string Empty);

public record RtsBody(string Name)
{
    public int Extra { get; init; } = 7;

    public int Computed => 42;

    public string Field = "f";

    private int _hidden = 1;

    public static int Static = 3;

    public const int Constant = 4;

    public int Hidden() => _hidden;
}

public record RtsAccess(int A)
{
    public int PrivGet { private get; init; } = 5;

    protected int Prot => 1;
}

public abstract record RtsVirtualBase
{
    public virtual int V => 1;
}

public record RtsVirtualDerived : RtsVirtualBase
{
    public override int V => 2;

    public new int W => 3;
}

public record RtsGeneric<T>(T Value);

public class RtsOuter
{
    public record Inner(int X);
}

public record RtsCycle(string Name)
{
    public RtsCycle? Next { get; set; }
}

public record RtsXDerived(string Name, int Level) : LPrintBase(Name);

public static class RecordToStringProbes
{
    public static string Flat() =>
        $"{new RtsItem("a", 1)}|{new RtsItem("b", 2).ToString()}|{"x" + new RtsItem("c", 3)}";

    public static string Nested() =>
        new RtsNested(new RtsItem("a", 1), "note", 'c').ToString()
        + "|"
        + new RtsGeneric<RtsItem>(new RtsItem("b", 2));

    public static string Collections() =>
        new RtsWithCollections(
            "n",
            new List<int> { 1 },
            new Dictionary<string, int> { ["k"] = 1 }
        ).ToString();

    public static string Derived()
    {
        RtsBase derived = new RtsDerived("n", 2);
        return $"{derived}|{new RtsSealedDerived("s", 0.5)}|{new RtsBase("b")}|{new RtsDerivedEmpty("e")}|{new RtsAfterEmptyBase(1)}";
    }

    public static string UserOverride() =>
        $"{new RtsCustom(1)}|{new RtsUserDerived(1, 2)}|{new RtsAbstractDerived(1, 2)}";

    public static string Sealed() =>
        $"{new RtsSealedRoot(1, "b")}|{new RtsSealedEmpty()}|{new RtsCustomDerived(1, 2)}|{new RtsEmpty()}";

    public static string NullMembers() =>
        new RtsNulls(null, null, null, null, null).ToString()
        + "|"
        + new RtsNulls("s", 1, new RtsItem("a", 1), RtsColor.Green, 0.5);

    public static string StringsAndChars() =>
        new RtsText("s", 'c', "").ToString() + "|" + new RtsGeneric<char>('z');

    public static string Numbers() =>
        new RtsNumbers(
            0.1f,
            1.0 / 3,
            1.50m,
            9007199254740993L,
            18446744073709551615UL,
            true,
            RtsColor.Green
        ).ToString();

    public static string RecordStructs() =>
        $"{new RtsPoint(1, 2)}|{new RtsReadOnlyPoint(0.1, 1e-7)}|{new RtsPoint(0, 0) with { Y = 3 }}";

    public static string MemberRules() =>
        $"{new RtsBody("x")}|{new RtsAccess(1)}|{new RtsVirtualDerived()}";

    public static string Names() =>
        $"{new RtsGeneric<int>(1)}|{new RtsGeneric<string?>(null)}|{new RtsOuter.Inner(1)}";

    public static string JoinAndTuple()
    {
        var items = new List<RtsItem> { new("a", 1), new("b", 2) };
        return string.Join(";", items) + "|" + (new RtsItem("a", 1), 2);
    }

    public static string CrossPackageDerived()
    {
        LPrintBase derived = new RtsXDerived("n", 2);
        return $"{derived}|{new LPrintBase("b")}";
    }

    public static string Cycle()
    {
        var cycle = new RtsCycle("a");
        cycle.Next = cycle;

        try
        {
            return cycle.ToString().Length > 1000 ? "long" : "short";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }
}
