namespace Metano.Compiler.IR;

/// <summary>
/// A C# numeric conversion the targets cannot leave implicit (spec 012):
/// one whose source or target is a 64-bit integer, implicit
/// (<c>long x = i</c>) or explicit (<c>(int)l</c>); an integer widened into
/// <c>BigInteger</c> (<c>BigInteger b = i</c>); a cast of a
/// floating-point value to a 32-bit or narrower integer
/// (<see cref="TruncatesFloatingPoint"/>, FR-070); or a cast between two
/// integers of 32 bits or fewer that can change the value
/// (<see cref="NarrowsInteger"/>, FR-074). The core decides
/// it from Roslyn's semantic model, so a target renders it from
/// <see cref="From"/>, <see cref="To"/> and <see cref="IsChecked"/> alone,
/// without re-deriving operand types.
/// </summary>
/// <param name="Operand">The converted value.</param>
/// <param name="From">The source endpoint: the numeric kind of <paramref name="Operand"/>.</param>
/// <param name="To">The target endpoint: the numeric kind of the result.</param>
/// <param name="IsChecked">Roslyn's <c>IConversionOperation.IsChecked</c>: the
/// conversion throws <c>OverflowException</c> when the value does not fit.</param>
/// <param name="IsLifted">Whether the conversion is lifted over nullable values
/// (<c>int?</c> to <c>long?</c>), so <c>null</c> passes through unchanged.</param>
/// <param name="CastType">The C# target type of the explicit cast this
/// conversion lowers (<c>null</c> for an implicit one), for a target that
/// keeps the cast where it does not convert the endpoints.</param>
public sealed record IrNumericConversion(
    IrExpression Operand,
    IrNumericEndpoint From,
    IrNumericEndpoint To,
    bool IsChecked,
    bool IsLifted = false,
    IrTypeRef? CastType = null
) : IrExpression
{
    /// <summary>
    /// A floating-point value cast to an integer of 32 bits or fewer, or to
    /// <c>char</c>: C# truncates it toward zero and saturates it to the
    /// <c>int</c> range (FR-070).
    /// </summary>
    public bool TruncatesFloatingPoint => IsFloatingPointTruncation(From, To);

    /// <summary>Whether a conversion from <paramref name="from"/> to <paramref name="to"/> truncates a floating-point value (FR-070).</summary>
    public static bool IsFloatingPointTruncation(IrNumericEndpoint from, IrNumericEndpoint to) =>
        !from.IsEnum
        && from.Kind.IsFloatingPoint()
        && (to.Kind.IsThirtyTwoBitOrNarrower() || to.Kind is IrNumericKind.Char);

    /// <summary>
    /// A conversion between two integers of 32 bits or fewer whose target
    /// does not hold every source value (<c>(byte)i</c>, <c>(uint)i</c>,
    /// <c>(int)u</c>, <c>(sbyte)b</c>): C# keeps the low bits of the target's
    /// width in an unchecked context and throws <c>OverflowException</c> in
    /// a checked one (FR-074). A widening (<c>(int)b</c>) is not one.
    /// </summary>
    public bool NarrowsInteger => IsIntegerNarrowing(From, To);

    /// <summary>Whether a conversion from <paramref name="from"/> to <paramref name="to"/> narrows an integer of 32 bits or fewer (FR-074).</summary>
    public static bool IsIntegerNarrowing(IrNumericEndpoint from, IrNumericEndpoint to) =>
        from.IsNarrowIntegerValue() && to.IsNarrowIntegerValue() && !to.Kind.HoldsEvery(from.Kind);
}

/// <summary>
/// One side of an <see cref="IrNumericConversion"/>. An enum endpoint reports
/// its underlying kind and sets <see cref="IsEnum"/>, so a target can tell
/// <c>(long)e</c> from <c>(long)i</c>.
/// </summary>
/// <param name="Kind">The exact numeric kind of the value.</param>
/// <param name="IsEnum">Whether the value is of an enum type.</param>
public sealed record IrNumericEndpoint(IrNumericKind Kind, bool IsEnum = false);

/// <summary>
/// Reduces the unbounded result of <see cref="Value"/> to the domain of the
/// integer kind <see cref="Kind"/>: wrapped modulo 2^n (n being the kind's
/// width) in an unchecked context, range-checked in a checked one. The core
/// reduces the overflowing 64-bit operations (addition, subtraction,
/// multiplication, negation, left shift, increments and their compound
/// forms, spec 012 FR-021 / FR-022) and, for the integers of 32 bits or
/// fewer (FR-075–FR-077): the value a compound assignment or an increment
/// stores in an <c>sbyte</c>, <c>byte</c>, <c>short</c> or <c>ushort</c>,
/// the <c>uint</c> result of a bitwise operator or a left shift, the
/// <c>int</c> result of <c>&gt;&gt;&gt;</c>, and <c>int</c> / <c>uint</c>
/// arithmetic in a checked context. Unchecked <c>int</c> / <c>uint</c>
/// arithmetic is not reduced (a documented gap).
/// </summary>
/// <param name="Value">The operation whose result may leave the domain.</param>
/// <param name="Kind">The integer kind of the result.</param>
/// <param name="Mode">How an out-of-range result is reduced.</param>
public sealed record IrIntegerOverflow(IrExpression Value, IrNumericKind Kind, IrOverflowMode Mode)
    : IrExpression;

/// <summary>How <see cref="IrIntegerOverflow"/> reduces an out-of-range result.</summary>
public enum IrOverflowMode
{
    /// <summary>An unchecked context: the result wraps modulo 2^n, n being the kind's width.</summary>
    Wrap,

    /// <summary>A checked context: the result throws <c>OverflowException</c>.</summary>
    Check,
}
