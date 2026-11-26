namespace Conformance;

// #801 explicit-constructor promotion: the parameter is promoted only when its store is in the
// constructor's leading run of unobserved stores (W10-10 follow-up of class-shape#1).

public class CpEarlyReturn
{
    public int Limit { get; }

    public CpEarlyReturn(int limit)
    {
        if (limit < 0)
            return;

        Limit = limit;
    }
}

public class CpReadFirst
{
    public int Limit { get; }
    public int Seen { get; }

    public CpReadFirst(int limit)
    {
        Seen = Limit;
        Limit = limit;
    }
}

public class CpOtherReceiver
{
    public int Limit { get; set; }
    public static CpOtherReceiver? Last;

    public CpOtherReceiver(int limit)
    {
        Last ??= this;
        Last.Limit = limit;
    }
}

public static class ConstructorPromotionProbes
{
    public static string StoreAfterAnEarlyReturn() =>
        $"{new CpEarlyReturn(-1).Limit}|{new CpEarlyReturn(2).Limit}";

    public static string StoreAfterAReadOfTheMember()
    {
        var a = new CpReadFirst(7);

        return $"{a.Seen}/{a.Limit}";
    }

    public static string StoreThroughAnotherReceiver()
    {
        CpOtherReceiver.Last = null;
        var root = new CpOtherReceiver(5);
        var child = new CpOtherReceiver(8);

        return $"{root.Limit}/{child.Limit}";
    }
}
