using Lib;

namespace Conformance;

/// <summary>
/// An owner whose nested types are hoisted to module level (spec 011 FR-040): the owner's
/// static initializer constructs a nested record, a nested class extends the owner and
/// constructs itself in its static initializer, and the nested types nest further.
/// </summary>
public class NestOwner
{
    public static readonly NestOwner Default = new(Part.Zero);

    public NestOwner(Part part)
    {
        Piece = part;
    }

    public Part Piece { get; }

    public virtual string Describe() => "owner:" + Piece.Value;

    public string Pick(Mode mode) => mode == Mode.On ? "on" : "off";

    public sealed record Part(int Value)
    {
        public static readonly Part Zero = new(7);

        public Part Twice() => new(Value * 2);
    }

    public enum Mode
    {
        Off,
        On,
    }

    public sealed class Holder<T>
    {
        public Holder(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }

    public sealed class Mid
    {
        public sealed record Leaf(Mode Mode);

        public static Leaf Make() => new(Mode.On);
    }

    public sealed class Child : NestOwner
    {
        public static readonly Child Instance = new();

        public Child()
            : base(new Part(3)) { }

        public override string Describe() => "child:" + Piece.Value;
    }
}

/// <summary>Nested types reached from another file and from another package.</summary>
public static class NestedProbes
{
    private static string B(bool b) => b ? "T" : "F";

    public static string Access()
    {
        var leaf = NestOwner.Mid.Make();

        return NestOwner.Default.Piece.Value
            + "|"
            + NestOwner.Part.Zero.Twice().Value
            + "|"
            + new NestOwner.Holder<string>("h").Value
            + "|"
            + (int)leaf.Mode
            + "|"
            + NestOwner.Default.Pick(NestOwner.Mode.On)
            + "|"
            + B(leaf == new NestOwner.Mid.Leaf(NestOwner.Mode.On));
    }

    public static string Ordering()
    {
        object child = NestOwner.Child.Instance;

        return NestOwner.Child.Instance.Describe()
            + "|"
            + B(child is NestOwner)
            + "|"
            + B(child is NestOwner.Child)
            + "|"
            + NestOwner.Default.Describe();
    }

    public static string CrossPackage()
    {
        object item = LNest.Item.Of(4);

        return LNest.Item.Of(4).N
            + "|"
            + (int)LNest.Tone.High
            + "|"
            + B(item is LNest.Item)
            + "|"
            + LNest.Item.Origin.N;
    }
}
