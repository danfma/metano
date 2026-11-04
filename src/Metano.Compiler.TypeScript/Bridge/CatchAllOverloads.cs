using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Finds the catch-all overloads of a dispatcher: an overload with parameters
/// none of which has a runtime check (each is a type parameter without a
/// checkable constraint) takes any arguments of its count, so a concrete
/// overload that <see cref="OverloadDispatchOrder"/> tries after it, and whose
/// argument-count range overlaps it, never sees the calls in the overlap. C#
/// picks the overload at compile time from the static argument types, and its
/// better-function rules can rank such a set cyclically, so no dispatch order
/// matches it; only static call-site dispatch (#25) does. The emitter reports
/// each one as <c>MS0063</c>.
/// </summary>
internal static class CatchAllOverloads
{
    /// <summary>
    /// Each catch-all overload, in dispatch order, with the overloads it
    /// shadows; empty when there is none.
    /// </summary>
    /// <param name="scopeOf">What an overload's argument checks resolve
    /// against.</param>
    public static IReadOnlyList<CatchAllOverload<TOverload>> Find<TOverload>(
        IEnumerable<TOverload> overloads,
        Func<TOverload, IReadOnlyList<IrParameter>> parametersOf,
        Func<TOverload, TypeCheckScope> scopeOf
    )
    {
        var sorted = OverloadDispatchOrder
            .Sort(overloads, parametersOf)
            .Select(overload =>
            {
                var parameters = parametersOf(overload);

                return new DispatchedOverload<TOverload>(
                    overload,
                    new OverloadArgumentShape(parameters),
                    ChecksNothing(parameters, scopeOf(overload))
                );
            })
            .ToList();

        return sorted
            .Select((overload, index) => (overload, index))
            .Where(entry => entry.overload.ChecksNothing)
            .Select(entry => new CatchAllOverload<TOverload>(
                entry.overload.Overload,
                ShadowedBy(entry.overload, sorted.Skip(entry.index + 1))
            ))
            .Where(found => found.Shadowed.Count > 0)
            .ToList();
    }

    private static IReadOnlyList<TOverload> ShadowedBy<TOverload>(
        DispatchedOverload<TOverload> catchAll,
        IEnumerable<DispatchedOverload<TOverload>> later
    ) =>
        later
            .Where(candidate =>
                !candidate.ChecksNothing && Overlaps(catchAll.Arguments, candidate.Arguments)
            )
            .Select(candidate => candidate.Overload)
            .ToList();

    /// <summary>
    /// Whether the overload has parameters and none of them carries a runtime
    /// check, so its guard is an argument count alone.
    /// </summary>
    private static bool ChecksNothing(
        IReadOnlyList<IrParameter> parameters,
        TypeCheckScope scope
    ) =>
        parameters.Count > 0
        && parameters.All(p => !IrTypeCheckBuilder.HasCheck(p.ParamsElementType ?? p.Type, scope));

    private static bool Overlaps(OverloadArgumentShape first, OverloadArgumentShape second) =>
        Math.Max(first.RequiredCount, second.RequiredCount)
        <= Math.Min(MaximumCountOf(first), MaximumCountOf(second));

    private static int MaximumCountOf(OverloadArgumentShape arguments) =>
        arguments.Rest is null ? arguments.FixedCount : int.MaxValue;

    private sealed record DispatchedOverload<TOverload>(
        TOverload Overload,
        OverloadArgumentShape Arguments,
        bool ChecksNothing
    );
}

/// <summary>
/// A catch-all overload and the concrete overloads, tried after it, whose
/// argument counts it overlaps.
/// </summary>
internal sealed record CatchAllOverload<TOverload>(
    TOverload Overload,
    IReadOnlyList<TOverload> Shadowed
);
