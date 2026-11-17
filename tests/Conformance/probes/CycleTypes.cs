namespace Conformance.Cyc;

public abstract class Node
{
    public string Describe() => Printer.Print(this);
}

public static class Printer
{
    public static string Print(Node n) => "leaf:" + ((Leaf)n).Text;
}

public class Leaf : Node
{
    public string Text = "t";
}

public static class CycleProbes
{
    public static string Run() => new Leaf().Describe();
}
