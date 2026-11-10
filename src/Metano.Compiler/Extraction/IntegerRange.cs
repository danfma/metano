using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A closed interval of integer values: the values an integer kind holds, or
/// the values an expression can produce. The small-integer lowering reads it
/// to emit nothing where a conversion or a reduction cannot change the value
/// (spec 012 FR-074–FR-077). The bounds are 128-bit, so the exact result of
/// any 64-bit operation fits.
/// </summary>
internal readonly record struct IntegerRange
{
    private IntegerRange(Int128 min, Int128 max)
    {
        Min = min;
        Max = max;
    }

    public Int128 Min { get; }

    public Int128 Max { get; }

    public bool IsNonNegative => Min >= 0;

    /// <summary>Whether every value of <paramref name="other"/> is in this range.</summary>
    public bool Contains(IntegerRange other) => Min <= other.Min && other.Max <= Max;

    public IntegerRange Plus(IntegerRange other) => new(Min + other.Min, Max + other.Max);

    public IntegerRange Minus(IntegerRange other) => new(Min - other.Max, Max - other.Min);

    public IntegerRange Times(IntegerRange other)
    {
        Int128[] products = [Min * other.Min, Min * other.Max, Max * other.Min, Max * other.Max];

        return new(products.Min(), products.Max());
    }

    public IntegerRange Negated() => new(-Max, -Min);

    /// <summary>
    /// <c>a &amp; b</c> over two non-negative ranges: no bit is set that
    /// either operand lacks, so the result is at most the smaller maximum.
    /// </summary>
    public IntegerRange BitwiseAnd(IntegerRange other) => new(0, Int128.Min(Max, other.Max));

    /// <summary>
    /// <c>a | b</c> or <c>a ^ b</c> over two non-negative ranges: no bit is
    /// set above the highest bit of either maximum.
    /// </summary>
    public IntegerRange BitwiseUnion(IntegerRange other)
    {
        var highest = Int128.Max(Max, other.Max);
        var bits = 128 - (int)Int128.LeadingZeroCount(highest);

        return new(0, (Int128.One << bits) - 1);
    }

    public static IntegerRange Exactly(Int128 value) => new(value, value);

    /// <summary>
    /// The values of the integer kind <paramref name="kind"/>; <c>null</c>
    /// for a kind that is not an integer of 64 bits or fewer.
    /// </summary>
    public static IntegerRange? Of(IrNumericKind kind)
    {
        if (!kind.IsInteger())
            return null;

        var width = kind.BitWidth();

        return kind.IsSigned()
            ? new(-(Int128.One << (width - 1)), (Int128.One << (width - 1)) - 1)
            : new(0, (Int128.One << width) - 1);
    }
}
