namespace Metano.Compiler.IR;

/// <summary>
/// How one parameter type is spelled in an overload name (spec 013 FR-002),
/// captured from the Roslyn symbol at extraction time, because the IR type
/// refs erase collections, delegates, unsigned primitives and containing types
/// (<c>List&lt;int&gt;</c> and <c>int[]</c> are one array type in the IR,
/// <c>uint</c> folds into <c>Int32</c>). The spelling is the concatenation of
/// its <see cref="Components"/>: <c>List&lt;A.Id&gt;</c> is
/// <c>List</c> + <c>Of</c> + <c>Id</c>, so <see cref="Leaf"/> is
/// <c>ListOfId</c>.
/// </summary>
/// <param name="Components">The spelling's parts in order. A part with an empty
/// <see cref="IrNamingComponent.Chain"/> never climbs: a primitive's CLR name
/// (<c>Int32</c>), a type parameter's name, an error type's <c>Value</c>, or a
/// structural word (<c>Of</c>, <c>Array</c>, <c>OrNull</c>, <c>TupleOf</c>,
/// <c>RefOf</c>). A named type carries its qualifier chain (FR-003).</param>
public sealed record IrNamingSpelling(IReadOnlyList<IrNamingComponent> Components)
{
    /// <summary>The unqualified spelling (<c>Int32</c>, <c>ListOfInt32</c>, <c>Decimal</c>).</summary>
    public string Leaf => string.Concat(Components.Select(component => component.Name));

    /// <summary>
    /// Whether both spellings name the same type: the same parts, each naming
    /// the same type declaration.
    /// </summary>
    public bool SpellsSameTypeAs(IrNamingSpelling other) =>
        Components.Count == other.Components.Count
        && Components.Zip(other.Components).All(pair => pair.First.NamesSameTypeAs(pair.Second));
}

/// <summary>
/// One part of an <see cref="IrNamingSpelling"/>.
/// </summary>
/// <param name="Name">The part's unqualified spelling, capitalized
/// (<c>Decimal</c>, <c>Of</c>, <c>Int32</c>).</param>
/// <param name="Chain">The qualifiers a named type climbs through, nearest
/// first: its containing types, then its namespace segments, then
/// <c>Global</c> (<c>A.B.Foo</c> → <c>B</c>, <c>A</c>, <c>Global</c>). Empty for
/// a part that never climbs.</param>
/// <param name="TypeIdentity">The named type declaration's identity (its
/// assembly and fully qualified name), which tells two declarations with the
/// same spelling apart; <c>null</c> for a part that never climbs.</param>
public sealed record IrNamingComponent(
    string Name,
    IReadOnlyList<string> Chain,
    string? TypeIdentity = null
)
{
    /// <summary>Whether the part can take <paramref name="level"/> + 1 qualifiers.</summary>
    public bool CanClimbFrom(int level) => level < Chain.Count;

    /// <summary>
    /// The part qualified by its nearest <paramref name="level"/> qualifiers,
    /// outermost first: <c>A.B.Foo</c> at level 2 is <c>ABFoo</c>.
    /// </summary>
    public string SpellAt(int level) => string.Concat(Chain.Take(level).Reverse()) + Name;

    /// <summary>Whether both parts are the same named type declaration.</summary>
    public bool NamesSameTypeAs(IrNamingComponent other) =>
        Name == other.Name
        && TypeIdentity == other.TypeIdentity
        && Chain.SequenceEqual(other.Chain, StringComparer.Ordinal);
}
