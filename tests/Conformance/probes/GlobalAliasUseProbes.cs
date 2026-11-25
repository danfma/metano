namespace Conformance
{
    /// <summary>
    /// A file that only spells a <c>global using</c> alias declared in another file
    /// imports the aliased type (#754 round 1).
    /// </summary>
    public static class GlobalAliasUseProbes
    {
        public static string GlobalAliasFromAnotherFile()
        {
            object plain = new GlobalAsNode();

            return (plain is GlobalAsNode) + "|" + new GlobalAsNode().X;
        }
    }
}
