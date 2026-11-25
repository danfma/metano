namespace Conformance.AliasScopeA
{
    public class AsNode<T>
    {
        public T Value;

        public AsNode(T value)
        {
            Value = value;
        }

        public string Who() => "generic";
    }
}

namespace Conformance.AliasScopeB
{
    public class AsNode
    {
        public int X = 7;

        public string Who() => "plain";
    }
}

namespace Conformance
{
    using AsNode = Conformance.AliasScopeB.AsNode;
    using GN = Conformance.AliasScopeA.AsNode<int>;

    /// <summary>
    /// Two <c>using</c> aliases of two same-named types each bind their own type, and an
    /// alias that spells its target's own name renames nothing (#754).
    /// </summary>
    public static class AliasScopeProbes
    {
        public static string TwoAliasesOfSameNamedTypes() =>
            new GN(5).Who() + new GN(5).Value + "|" + new AsNode().Who() + new AsNode().X;
    }
}

namespace Conformance
{
    using BNode = Conformance.AliasScopeB.AsNode;

    /// <summary>
    /// A <c>using</c> alias applies to the type it names only: a same-named type the
    /// file spells keeps its own module (#754).
    /// </summary>
    public static class AliasScopeSpelledProbes
    {
        public static string AliasBesideSpelledSameName() =>
            new BNode().Who() + "|" + new AliasScopeA.AsNode<int>(3).Who();
    }
}
