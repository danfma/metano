namespace Lib;

// FR-113: the self-typed statics of a CRTP base, published by an [EmitPackage] library, so a
// hidden reified parameter is forbidden (W5-5d). CrtpStaticProbes calls them from the consumer.
public abstract class CrtpComponent<TSelf>
    where TSelf : CrtpComponent<TSelf>, new()
{
    public string Title { get; set; } = "";

    public static TSelf Use(Action<TSelf> build)
    {
        var component = new TSelf();
        build(component);
        return component;
    }

    public static TSelf Use(string title) => Use(c => c.Title = title);

    public static TSelf Titled() => Use(c => c.Title = "titled");

    public static TSelf Twice()
    {
        var first = new TSelf();
        return Use(c => c.Title = first.Title + "second");
    }
}

public class CrtpCard : CrtpComponent<CrtpCard>
{
    public CrtpCard() => CrtpLog.Entries.Add("card");
}

public class CrtpFancy : CrtpCard
{
    public CrtpFancy() => CrtpLog.Entries.Add("fancy");
}

public class CrtpBox<T> : CrtpComponent<CrtpBox<T>>
{
    public T? Value { get; set; }
}

public abstract class CrtpMid<TSelf> : CrtpComponent<TSelf>
    where TSelf : CrtpMid<TSelf>, new()
{
    public static TSelf Make() => Use(c => c.Title = "mid");
}

public class CrtpLeaf : CrtpMid<CrtpLeaf> { }

public class CrtpWidget<TSelf>
    where TSelf : CrtpWidget<TSelf>, new()
{
    public virtual string Kind => "widget";

    public static TSelf Make() => new TSelf();
}

public class CrtpButton : CrtpWidget<CrtpButton>
{
    public override string Kind => "button";
}

public static class CrtpLog
{
    public static List<string> Entries { get; } = [];

    public static string Drain()
    {
        var text = string.Join(",", Entries);
        Entries.Clear();
        return text;
    }
}
