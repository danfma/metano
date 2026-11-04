using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The order a dispatcher tries its overloads in. The first branch whose
/// guard matches wins, and the guards of two overloads can overlap once
/// optional arguments, <c>params</c> arrays or type parameters (which check
/// nothing) are involved, so the order approximates C#'s better-function
/// rules:
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
/// Within the same tier and parameter count, an overload with fewer
/// type-parameter-typed parameters comes first, since C# prefers the less
/// generic overload (<c>F(int)</c> over <c>F&lt;T&gt;(T)</c>) and a type
/// parameter's guard is the loosest. The sort is stable, so remaining ties
/// keep declaration order.
/// <para>
/// No order matches C# when a catch-all overload (no parameter checked) shares
/// argument counts with a concrete optional or <c>params</c> one, since C#
/// can rank such sets cyclically; <see cref="CatchAllOverloads"/> reports
/// them as <c>MS0063</c> until static call-site dispatch (#25) lands.
/// </para>
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
            .ThenBy(entry => entry.Rank.ParameterCountOrder)
            .ThenBy(entry => entry.Rank.TypeParameterArguments)
            .Select(entry => entry.Overload)
            .ToList();

    /// <summary>The tiers, in dispatch order.</summary>
    private enum DispatchTier
    {
        /// <summary>Every parameter is required.</summary>
        Required,

        /// <summary>Some parameter can be omitted, none is <c>params</c>.</summary>
        Optional,

        /// <summary>The last parameter is <c>params</c>.</summary>
        Params,
    }

    /// <summary>
    /// The sort keys of one overload.
    /// </summary>
    /// <param name="Tier">Its <see cref="DispatchTier"/>.</param>
    /// <param name="ParameterCountOrder">Its parameter count, negated outside
    /// the optional tier so an ascending sort puts the most parameters first
    /// there and the fewest first in the optional tier.</param>
    /// <param name="TypeParameterArguments">Its type-parameter-typed
    /// parameters.</param>
    private sealed record OverloadRank(
        DispatchTier Tier,
        int ParameterCountOrder,
        int TypeParameterArguments
    )
    {
        public static OverloadRank Of(IReadOnlyList<IrParameter> parameters)
        {
            var tier = TierOf(new OverloadArgumentShape(parameters));
            var countOrder = tier is DispatchTier.Optional ? parameters.Count : -parameters.Count;

            return new OverloadRank(
                tier,
                countOrder,
                parameters.Count(p => IsTypeParameter(p.Type))
            );
        }

        private static DispatchTier TierOf(OverloadArgumentShape arguments)
        {
            if (arguments.Rest is not null)
                return DispatchTier.Params;

            return arguments.HasOptionalArguments ? DispatchTier.Optional : DispatchTier.Required;
        }

        private static bool IsTypeParameter(IrTypeRef type) =>
            type switch
            {
                IrTypeParameterRef => true,
                IrNullableTypeRef nullable => IsTypeParameter(nullable.Inner),
                IrArrayTypeRef array => IsTypeParameter(array.ElementType),
                _ => false,
            };
    }
}
