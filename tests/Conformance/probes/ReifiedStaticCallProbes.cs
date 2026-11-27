namespace Conformance;

// Internal on purpose: a public type of this [EmitPackage] assembly is always described, and
// a public static could not carry the hidden reified parameter (W5-5d). Internal types are
// described only when something reads their descriptor, which is what #834 is about.
internal abstract class ReifiedWidget<TSelf>
    where TSelf : ReifiedWidget<TSelf>, new()
{
    public string Title { get; set; } = "";

    public abstract string Kind { get; }

    public static TSelf Use(Action<TSelf> build)
    {
        var widget = new TSelf();
        build(widget);
        return widget;
    }
}

internal sealed class ReifiedCard : ReifiedWidget<ReifiedCard>
{
    public override string Kind => "card";
}

public static class ReifiedStaticCallProbes
{
    public static string StaticCallOnGenericBaseConstructsTheTypeArgument()
    {
        var card = ReifiedCard.Use(c => c.Title = "x");
        return card.Kind + ":" + card.Title;
    }
}
