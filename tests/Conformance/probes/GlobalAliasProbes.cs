global using GlobalAsNode = Conformance.AliasScopeB.AsNode;

namespace Conformance
{
    /// <summary>
    /// A <c>global using</c> alias declared in this file reaches the probes of every
    /// file and binds the type it names, not a same-named type of another namespace
    /// (#754 round 1).
    /// </summary>
    public static class GlobalAliasProbes
    {
        public static string GlobalAliasTypeTest()
        {
            object plain = new GlobalAsNode();
            object generic = new AliasScopeA.AsNode<int>(1);

            return (plain is GlobalAsNode)
                + "|"
                + (generic is GlobalAsNode)
                + "|"
                + new GlobalAsNode().Who();
        }
    }
}
