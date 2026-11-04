using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The order a dispatcher tries its overloads in. The first branch whose
/// guard matches wins, and the guards of two overloads can overlap once
/// optional arguments or <c>params</c> arrays are involved, so the order
/// approximates C#'s better-function rules:
/// <list type="number">
///   <item>Overloads whose every parameter is required come first, most
///   parameters first (the legacy order; their exact-count guards never
///   overlap across counts). A call that passes every argument beats one
///   that needs a default.</item>
///   <item>Overloads with optional parameters follow, fewest parameters
///   first: for a given argument count, the overload that needs the fewest
///   defaults is tried first.</item>
///   <item><c>params</c> overloads come last, since C# prefers a normal-form
///   match to an expanded one; most declared parameters first.</item>
/// </list>
/// The sort is stable, so ties keep declaration order.
/// </summary>
internal static class OverloadDispatchOrder
{
    public static List<TOverload> Sort<TOverload>(
        IEnumerable<TOverload> overloads,
        Func<TOverload, IReadOnlyList<IrParameter>> parametersOf
    ) =>
        overloads
            .Select(overload => (Overload: overload, Rank: OverloadRank.Of(parametersOf(overload))))
            .OrderBy(entry => entry.Rank.Tier)
            .ThenBy(entry => entry.Rank.CountKey)
            .Select(entry => entry.Overload)
            .ToList();

    /// <summary>
    /// The sort keys of one overload: its tier (required-only 0, optional 1,
    /// <c>params</c> 2), its parameter count (ascending in the optional tier,
    /// descending otherwise).
    /// </summary>
    private sealed record OverloadRank(int Tier, int CountKey)
    {
        private const int RequiredTier = 0;
        private const int OptionalTier = 1;
        private const int ParamsTier = 2;

        public static OverloadRank Of(IReadOnlyList<IrParameter> parameters)
        {
            var tier = TierOf(new OverloadArgumentShape(parameters));
            var countKey = tier == OptionalTier ? parameters.Count : -parameters.Count;

            return new OverloadRank(tier, countKey);
        }

        private static int TierOf(OverloadArgumentShape arguments)
        {
            if (arguments.Rest is not null)
                return ParamsTier;

            return arguments.HasOptionalArguments ? OptionalTier : RequiredTier;
        }
    }
}
