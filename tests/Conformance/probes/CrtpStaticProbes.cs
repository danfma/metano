using Lib;

namespace Conformance;

// FR-113: every spelling of a call to a self-typed static builds the class bound to the self
// parameter, as .NET does (a further-derived receiver still builds the base's self argument).
public static class CrtpStaticProbes
{
    public static string ThroughTheDerivedType()
    {
        var card = CrtpCard.Use(c => c.Title = "d");
        return card.GetType().Name + ":" + card.Title + ":" + CrtpLog.Drain();
    }

    public static string ThroughTheBase()
    {
        var card = CrtpComponent<CrtpCard>.Use(c => c.Title = "b");
        return card.GetType().Name + ":" + card.Title + ":" + CrtpLog.Drain();
    }

    public static string ThroughAFurtherDerivedType()
    {
        var card = CrtpFancy.Use(c => c.Title = "f");

        return card.GetType().Name
            + ":"
            + card.Title
            + ":"
            + (card is CrtpFancy)
            + ":"
            + CrtpLog.Drain();
    }

    public static string Forwarding()
    {
        var titled = CrtpFancy.Titled();
        var twice = CrtpCard.Twice();

        return titled.GetType().Name
            + ":"
            + titled.Title
            + "|"
            + twice.GetType().Name
            + ":"
            + twice.Title
            + ":"
            + CrtpLog.Drain();
    }

    public static string OverloadSelection()
    {
        var byTitle = CrtpCard.Use("t");
        var byBuild = CrtpComponent<CrtpCard>.Use(c => c.Title = "u");
        return byTitle.Title + "," + byBuild.Title + ":" + CrtpLog.Drain();
    }

    public static string GenericSelfArgument()
    {
        var box = CrtpComponent<CrtpBox<int>>.Use(b => b.Value = 7);
        return box.GetType().Name + ":" + box.Value;
    }

    public static string ThroughAnIntermediateClass()
    {
        var leaf = CrtpLeaf.Make();

        return leaf.GetType().Name + ":" + leaf.Title;
    }

    // FR-113 W15-2d known gap: an open self argument cannot name a receiving class, so the
    // call keeps its receiver (MS0054 warning) and builds the concrete base class.
    public static string OpenSelfOnAConcreteBase() => MakeWidget<CrtpButton>();

    private static string MakeWidget<T>()
        where T : CrtpWidget<T>, new() => CrtpWidget<T>.Make().Kind;
}
