namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The cast state of one file group's transform, carried by
/// <see cref="IrToTsBridgeContext.Casts"/>: casts to the classes named in
/// <see cref="Asserted"/> are asserted, and every local class a cast checks
/// is collected into <see cref="Checked"/> for the import-graph pass
/// (<see cref="ReferenceCastLowering"/>).
/// </summary>
internal sealed class GroupCasts(IReadOnlySet<string> asserted)
{
    /// <summary>The classes whose value import would close an import cycle, by emitted name.</summary>
    public IReadOnlySet<string> Asserted { get; } = asserted;

    /// <summary>The local classes the group's casts check with <c>castTo</c>, by emitted name.</summary>
    public HashSet<string> Checked { get; } = new(StringComparer.Ordinal);
}
