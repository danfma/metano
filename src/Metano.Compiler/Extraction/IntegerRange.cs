using Metano.Compiler.IR;

namespace Metano.Compiler.Extraction;

/// <summary>
/// A closed interval of integer values: the values an integer kind holds, or
/// the values an expression can produce. The small-integer lowering reads it
/// to emit nothing where a conversion or a reduction cannot change the value
/// (spec 012 FR-074–FR-077). The bounds are 128-bit: they hold every 64-bit
/// value and every sum or difference of two, but not every product
/// (<c>ulong.MaxValue * ulong.MaxValue</c> needs 128 unsigned bits), so an
/// operator whose bound cannot be stated answers <c>null</c>, "unknown".
/// </summary>
internal readonly record struct IntegerRange
{
    private const int BitsOfInt128 = 128;
    private const int UnmaskedShiftCounts = 32;

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

    /// <summary>
    /// The products of the two ranges; <c>null</c> when a bound does not fit
    /// 128 signed bits.
    /// </summary>
    public IntegerRange? Times(IntegerRange other)
    {
        try
        {
            Int128[] products = checked(
                [Min * other.Min, Min * other.Max, Max * other.Min, Max * other.Max]
            );

            return new(products.Min(), products.Max());
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public IntegerRange Negated() => new(-Max, -Min);

    /// <summary>
    /// <c>a &amp; b</c>: the result has no bit a non-negative operand lacks,
    /// so one non-negative operand bounds it whatever the other holds (two
    /// bound it by the smaller maximum); <c>null</c> when both operands can
    /// be negative.
    /// </summary>
    public IntegerRange? BitwiseAnd(IntegerRange other) =>
        (IsNonNegative, other.IsNonNegative) switch
        {
            (true, true) => new(0, Int128.Min(Max, other.Max)),
            (true, false) => new(0, Max),
            (false, true) => new(0, other.Max),
            (false, false) => null,
        };

    /// <summary>
    /// <c>a | b</c> or <c>a ^ b</c>: over two non-negative ranges no bit is
    /// set above the highest bit of either maximum; <c>null</c> when an
    /// operand can be negative.
    /// </summary>
    public IntegerRange? BitwiseUnion(IntegerRange other)
    {
        if (!IsNonNegative || !other.IsNonNegative)
            return null;

        var highest = Int128.Max(Max, other.Max);
        var bits = BitsOfInt128 - (int)Int128.LeadingZeroCount(highest);

        return new(0, (Int128.One << bits) - 1);
    }

    /// <summary>
    /// <c>a &lt;&lt; count</c> before any bit is dropped, for a count of
    /// <c>0</c> to <c>31</c>, which every integer shift reads as it is;
    /// <c>null</c> for any other count, which C# masks by the operand's width.
    /// </summary>
    public IntegerRange? ShiftedLeft(int count) =>
        count is >= 0 and < UnmaskedShiftCounts ? new(Min << count, Max << count) : null;

    public static IntegerRange Exactly(Int128 value) => new(value, value);

    /// <summary>The values of the integer kind <paramref name="kind"/> (64 bits or fewer).</summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not an integer kind.</exception>
    public static IntegerRange Of(IrNumericKind kind)
    {
        if (!kind.IsInteger())
            throw new ArgumentException($"'{kind}' is not an integer kind.", nameof(kind));

        var width = kind.BitWidth();

        return kind.IsSigned()
            ? new(-(Int128.One << (width - 1)), (Int128.One << (width - 1)) - 1)
            : new(0, (Int128.One << width) - 1);
    }
}
