namespace Metano.Compiler.Extraction;

/// <summary>
/// What the runtime pipe operator of a LINQ operator implements (#648): the
/// .NET overloads it lowers and the argument position at which it reads a
/// queryable expression tree, if any. It owns the rule of where a stage's
/// tree lands: after the stage's arguments, or, for an operator that pads
/// the arguments a stage omits (the <c>OrDefault</c> terminals), at its slot.
/// </summary>
/// <remarks>
/// TODO(#657): this describes the TypeScript runtime; it belongs to the
/// TypeScript target, behind a port, not to the core.
/// </remarks>
public sealed record IrLinqRuntimeSignature
{
    private IrLinqRuntimeSignature(
        IReadOnlyList<LinqOverloadParameters> overloads,
        int? queryableSlot,
        bool padsOmittedArguments
    )
    {
        Overloads = overloads;
        QueryableSlot = queryableSlot;
        PadsOmittedArguments = padsOmittedArguments;
    }

    /// <summary>The implemented overloads.</summary>
    public IReadOnlyList<LinqOverloadParameters> Overloads { get; }

    /// <summary>
    /// The argument position of the operator's <c>QueryableMeta</c>
    /// parameter, or <c>null</c> when it reads no tree.
    /// </summary>
    public int? QueryableSlot { get; }

    /// <summary>
    /// Whether a stage passes <c>undefined</c> for the arguments it omits
    /// before the tree, so the tree reaches <see cref="QueryableSlot"/>.
    /// </summary>
    public bool PadsOmittedArguments { get; }

    /// <summary>Whether the operator implements <paramref name="overload"/>.</summary>
    public bool Implements(LinqOverloadParameters overload) => Overloads.Contains(overload);

    /// <summary>
    /// How many <c>undefined</c> arguments a stage passing
    /// <paramref name="argumentCount"/> arguments puts before its tree.
    /// </summary>
    public int ArgumentsOmittedBeforeTree(int argumentCount) =>
        PadsOmittedArguments && QueryableSlot is { } slot ? Math.Max(0, slot - argumentCount) : 0;

    /// <summary>
    /// Whether the operator reads the tree a stage passes after its
    /// <paramref name="argumentCount"/> arguments.
    /// </summary>
    public bool ReadsTreeAfter(int argumentCount) =>
        QueryableSlot is { } slot
        && argumentCount + ArgumentsOmittedBeforeTree(argumentCount) == slot;

    /// <summary>
    /// Whether the operator reads a tree, but not where a stage passing
    /// <paramref name="argumentCount"/> arguments puts it, so it would read
    /// the tree as another argument. An operator that reads no tree is
    /// passed none.
    /// </summary>
    public bool MisplacesTreeAfter(int argumentCount) =>
        QueryableSlot is not null && !ReadsTreeAfter(argumentCount);

    /// <summary>
    /// The same signature, padding the arguments a stage omits up to its
    /// queryable slot.
    /// </summary>
    public IrLinqRuntimeSignature PaddingOmittedArguments() =>
        new(Overloads, QueryableSlot, padsOmittedArguments: true);

    public bool Equals(IrLinqRuntimeSignature? other) =>
        other is not null
        && QueryableSlot == other.QueryableSlot
        && PadsOmittedArguments == other.PadsOmittedArguments
        && Overloads.SequenceEqual(other.Overloads);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(QueryableSlot);
        hash.Add(PadsOmittedArguments);

        foreach (var overload in Overloads)
            hash.Add(overload);

        return hash.ToHashCode();
    }

    /// <summary>
    /// A signature implementing <paramref name="overloads"/> and reading its
    /// tree at <paramref name="queryableSlot"/>.
    /// </summary>
    public static IrLinqRuntimeSignature Of(
        int? queryableSlot,
        params LinqOverloadParameters[] overloads
    )
    {
        if (queryableSlot < 0)
            throw new ArgumentOutOfRangeException(nameof(queryableSlot), "A slot is a position.");

        if (overloads.Length == 0)
        {
            throw new ArgumentException(
                "A runtime operator implements an overload.",
                nameof(overloads)
            );
        }

        if (overloads.Distinct().Count() != overloads.Length)
            throw new ArgumentException("An overload is declared twice.", nameof(overloads));

        return new IrLinqRuntimeSignature(
            [.. overloads],
            queryableSlot,
            padsOmittedArguments: false
        );
    }
}
