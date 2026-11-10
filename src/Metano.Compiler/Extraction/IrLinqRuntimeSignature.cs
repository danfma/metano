namespace Metano.Compiler.Extraction;

/// <summary>
/// What the runtime pipe operator of a LINQ operator implements (#648): the
/// .NET overloads it lowers, each named by its parameters after the source
/// (<c>(keySelector)</c>, <c>(seed, func)</c>), and the position at which it
/// reads a queryable expression tree, <c>null</c> when it reads none.
/// </summary>
/// <param name="Overloads">The parameter names of each implemented overload,
/// in declaration order, without the source.</param>
/// <param name="QueryableSlot">The argument position of the operator's
/// <c>QueryableMeta</c> parameter, or <c>null</c> when it has none.</param>
public sealed record IrLinqRuntimeSignature(
    IReadOnlyList<IReadOnlyList<string>> Overloads,
    int? QueryableSlot
)
{
    /// <summary>Whether the overload named by <paramref name="parameters"/> is implemented.</summary>
    public bool Implements(IReadOnlyList<string> parameters) =>
        Overloads.Any(overload => overload.SequenceEqual(parameters, StringComparer.Ordinal));

    /// <summary>
    /// A signature whose <paramref name="overloads"/> each list their
    /// parameter names separated by commas (<c>"seed, func"</c>; <c>""</c>
    /// for none).
    /// </summary>
    public static IrLinqRuntimeSignature Of(int? queryableSlot, params string[] overloads) =>
        new(
            [
                .. overloads.Select(overload =>
                    (IReadOnlyList<string>)
                        overload.Split(
                            ',',
                            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                        )
                ),
            ],
            queryableSlot
        );
}
