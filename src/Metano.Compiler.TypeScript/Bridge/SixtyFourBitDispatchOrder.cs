using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Reorders the sibling sets of the staging BigInt mode's dispatchers into the
/// documented dispatch order of spec 012 FR-048 (OQ-6). A sibling set is a
/// group of same-arity overloads whose parameter types are identical except at
/// positions where both sides are 64-bit integers (<c>long</c>, <c>ulong</c>,
/// <c>BigInteger</c>, nullable included): their guards all accept an in-range
/// <c>bigint</c>, so C#'s static binding is unobservable at a runtime call and
/// the documented order decides — <c>long</c>, then <c>ulong</c>, then
/// <c>BigInteger</c>, per position, lexicographically.
/// </summary>
/// <remarks>
/// Only the members of such a set move, and only into the slots the set
/// already occupies, so an overload that names a non-64-bit parameter where a
/// neighbor names a 64-bit one (a branded value, <c>object</c>,
/// <c>string</c>), or an overload outside any set, keeps the declaration order
/// the legacy sort produced. Members of a set that rank the same
/// (<c>M(long)</c> beside <c>M(long?)</c>) keep declaration order too. The
/// reorder runs only in the staging BigInt mode — its caller
/// (<see cref="OverloadDispatchOrder.Sort"/>) gates on
/// <see cref="Int64RepresentationScope"/>, and the Number mode keeps the
/// shipped output byte for byte (NFR-001).
/// </remarks>
internal static class SixtyFourBitDispatchOrder
{
    /// <summary>The OQ-6 dispatch ranks, lowest tried first.</summary>
    private const int LongRank = 1;
    private const int ULongRank = 2;
    private const int BigIntegerRank = 3;

    /// <summary>
    /// Puts every sibling set of <paramref name="sorted"/> (the legacy sort's
    /// output) into the OQ-6 order, in place; the other members keep their
    /// slots. Sets only form inside the contiguous same-arity blocks the
    /// legacy sort produces, so an arity is never reordered against another.
    /// </summary>
    public static List<TOverload> Reordered<TOverload>(
        List<TOverload> sorted,
        Func<TOverload, IReadOnlyList<IrParameter>> parametersOf
    )
    {
        var result = new List<TOverload>(sorted);
        var blockStart = 0;

        while (blockStart < sorted.Count)
        {
            var blockEnd = EndOfSameArityBlock(sorted, parametersOf, blockStart);

            ReorderBlock(result, sorted, parametersOf, blockStart, blockEnd);
            blockStart = blockEnd;
        }

        return result;
    }

    /// <summary>The end (exclusive) of the run of overloads sharing the arity of <paramref name="start"/>.</summary>
    private static int EndOfSameArityBlock<TOverload>(
        List<TOverload> sorted,
        Func<TOverload, IReadOnlyList<IrParameter>> parametersOf,
        int start
    )
    {
        var arity = parametersOf(sorted[start]).Count;
        var end = start + 1;

        while (end < sorted.Count && parametersOf(sorted[end]).Count == arity)
        {
            end++;
        }

        return end;
    }

    /// <summary>Reorders the sibling sets of one same-arity block in place.</summary>
    private static void ReorderBlock<TOverload>(
        List<TOverload> result,
        List<TOverload> block,
        Func<TOverload, IReadOnlyList<IrParameter>> parametersOf,
        int start,
        int end
    )
    {
        var grouped = new Dictionary<SignatureKey, List<int>>();

        for (var index = start; index < end; index++)
        {
            var key = SignatureKey.Of(parametersOf(block[index]));

            if (!key.HasSixtyFourBitPosition)
            {
                continue;
            }

            if (!grouped.TryGetValue(key, out var members))
            {
                members = [];
                grouped[key] = members;
            }

            members.Add(index);
        }

        foreach (var members in grouped.Values)
        {
            if (members.Count < 2)
            {
                continue;
            }

            // The members list is already in ascending slot order (the block
            // walk appends in order), and OrderBy is stable, so equal ranks
            // keep the declaration order.
            var ordered = members
                .OrderBy(index => RankVector.Of(parametersOf(block[index])))
                .ToList();

            for (var slot = 0; slot < members.Count; slot++)
            {
                result[members[slot]] = block[ordered[slot]];
            }
        }
    }

    /// <summary>
    /// A parameter list with every 64-bit position erased: two overloads are
    /// siblings exactly when their keys are equal. A position that is not
    /// 64-bit keeps the type itself, so a neighbor's branded value,
    /// <c>object</c> or <c>string</c> keeps the pair apart. Named types with
    /// generic arguments compare their argument lists by reference, which can
    /// keep apart two semantically identical keys — the conservative side.
    /// </summary>
    private sealed class SignatureKey : IEquatable<SignatureKey>
    {
        private readonly IReadOnlyList<IrTypeRef?> _positions;

        private SignatureKey(IReadOnlyList<IrTypeRef?> positions) => _positions = positions;

        /// <summary>Whether at least one position is a 64-bit integer.</summary>
        public bool HasSixtyFourBitPosition => _positions.Any(position => position is null);

        public static SignatureKey Of(IReadOnlyList<IrParameter> parameters)
        {
            var positions = new IrTypeRef?[parameters.Count];

            for (var position = 0; position < positions.Length; position++)
            {
                positions[position] = SixtyFourBitDispatchOrder.RankOf(parameters[position].Type)
                    is 0
                    ? parameters[position].Type
                    : null;
            }

            return new SignatureKey(positions);
        }

        public bool Equals(SignatureKey? other)
        {
            if (other is null || _positions.Count != other._positions.Count)
            {
                return false;
            }

            for (var position = 0; position < _positions.Count; position++)
            {
                if (!Equals(_positions[position], other._positions[position]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as SignatureKey);

        public override int GetHashCode()
        {
            var hash = new HashCode();

            foreach (var position in _positions)
            {
                hash.Add(position);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>The OQ-6 rank of each position; non-64-bit positions rank <c>0</c>.</summary>
    private readonly struct RankVector : IComparable<RankVector>
    {
        private readonly int[] _ranks;

        private RankVector(int[] ranks) => _ranks = ranks;

        public static RankVector Of(IReadOnlyList<IrParameter> parameters)
        {
            var ranks = new int[parameters.Count];

            for (var position = 0; position < ranks.Length; position++)
            {
                ranks[position] = SixtyFourBitDispatchOrder.RankOf(parameters[position].Type);
            }

            return new RankVector(ranks);
        }

        public int CompareTo(RankVector other)
        {
            var shared = Math.Min(_ranks.Length, other._ranks.Length);

            for (var position = 0; position < shared; position++)
            {
                if (_ranks[position] != other._ranks[position])
                {
                    return _ranks[position].CompareTo(other._ranks[position]);
                }
            }

            return 0;
        }
    }

    /// <summary>The OQ-6 dispatch rank of a parameter type; <c>0</c> when it is not a 64-bit integer.</summary>
    private static int RankOf(IrTypeRef type) =>
        SixtyFourBitTypes.PlainPrimitiveOf(type) switch
        {
            IrPrimitive.Int64 => LongRank,
            IrPrimitive.UInt64 => ULongRank,
            IrPrimitive.BigInteger => BigIntegerRank,
            _ => 0,
        };
}
