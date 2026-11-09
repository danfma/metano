using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Finds the overload sets a dispatcher cannot tell apart because
/// a branded 64-bit type sits beside another <c>bigint</c>-valued type: the
/// branded arm is a <c>typeof === "bigint"</c> test, which accepts every
/// <c>bigint</c> the <c>long</c> / <c>ulong</c> / <c>BigInteger</c> guards
/// accept, so a call that reaches the dispatcher can run another overload than
/// the one C# binds. A branded type is a <c>[Branded]</c> or
/// <c>[InlineWrapper]</c> wrapper over <c>long</c>, <c>ulong</c>,
/// <c>nint</c>, <c>nuint</c> or <c>BigInteger</c>.
/// <para>
/// A set is a group of same-arity overloads whose parameter types are
/// identical except at positions where each side is <c>bigint</c>-valued
/// (a 64-bit integer or a branded wrapper over one, nullable included, a
/// <c>params</c> array by its element type) and which holds a branded member.
/// Groups without a branded member are the documented OQ-6 order
/// (<see cref="SixtyFourBitDispatchOrder"/>), and a branded type over a
/// non-64-bit primitive checks a different <c>typeof</c>, so neither is
/// reported. The emitter reports each group as <c>MS0063</c>.
/// </para>
/// </summary>
internal static class BrandedSixtyFourBitOverloads
{
    /// <summary>
    /// Each ambiguous group, members in the order given; empty when there is
    /// none.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TOverload>> Find<TOverload>(
        IEnumerable<TOverload> overloads,
        Func<TOverload, IReadOnlyList<IrParameter>> parametersOf
    )
    {
        return overloads
            .Select(overload => new Candidate<TOverload>(
                overload,
                PositionsOf(parametersOf(overload))
            ))
            .Where(candidate => candidate.Positions.Any(position => position.IsBigIntValued))
            .GroupBy(candidate => candidate.Signature)
            .Select(group => group.ToList())
            .Where(group =>
                group.Count > 1 && group.Any(candidate => candidate.Positions.Any(p => p.IsBranded))
            )
            .Select(group => (IReadOnlyList<TOverload>)group.Select(c => c.Overload).ToList())
            .ToList();
    }

    private static List<Position> PositionsOf(IReadOnlyList<IrParameter> parameters) =>
        parameters.Select(p => Position.Of(p.ParamsElementType ?? p.Type)).ToList();

    private static bool IsBrandedBigIntValued(IrTypeRef type) =>
        type switch
        {
            IrNamedTypeRef
            {
                Semantics:
                { Kind: IrNamedTypeKind.Branded, BrandedUnderlyingPrimitive: { } primitive },
            } => SixtyFourBitTypes.IsBigIntValued(primitive),
            IrNullableTypeRef nullable => IsBrandedBigIntValued(nullable.Inner),
            _ => false,
        };

    /// <summary>
    /// One parameter position: <see cref="Key"/> is its type's signature, or
    /// the shared <see cref="BigIntKey"/> when the position is
    /// <c>bigint</c>-valued, so two overloads are in one group exactly when
    /// their keys are equal.
    /// </summary>
    private sealed record Position(string Key, bool IsBranded, bool IsBigIntValued)
    {
        /// <summary>A key no <see cref="IrTypeRefSignatures.Describe"/> result can equal.</summary>
        private const string BigIntKey = "\0bigint";

        public static Position Of(IrTypeRef type)
        {
            var isBranded = IsBrandedBigIntValued(type);
            var isBigIntValued = isBranded || SixtyFourBitTypes.PlainPrimitiveOf(type) is not null;

            return new Position(
                isBigIntValued ? BigIntKey : IrTypeRefSignatures.Describe(type),
                isBranded,
                isBigIntValued
            );
        }
    }

    private sealed record Candidate<TOverload>(TOverload Overload, List<Position> Positions)
    {
        public string Signature { get; } = string.Join("|", Positions.Select(p => p.Key));
    }
}
