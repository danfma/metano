namespace Metano.Compiler.IR;

/// <summary>
/// A C# numeric conversion the targets cannot leave implicit (spec 012):
/// one whose source or target is a 64-bit integer, implicit
/// (<c>long x = i</c>) or explicit (<c>(int)l</c>); an integer widened into
/// <c>BigInteger</c> (<c>BigInteger b = i</c>); or a cast of a
/// floating-point value to a 32-bit or narrower integer
/// (<see cref="TruncatesFloatingPoint"/>, FR-070). The core decides
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
        && (to.Kind.IsInteger() || to.Kind is IrNumericKind.Char)
        && to.Kind.BitWidth() <= 32;
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
/// Reduces the unbounded result of <see cref="Value"/> to the domain of
/// <see cref="Kind"/> (<see cref="IrNumericKind.Int64"/> or
/// <see cref="IrNumericKind.UInt64"/>): wrapped modulo 2^64 in an unchecked
/// context, range-checked in a checked one. The core wraps the overflowing
/// 64-bit operations only (addition, subtraction, multiplication, negation,
/// left shift, increments and their compound forms).
/// </summary>
/// <param name="Value">The operation whose result may leave the domain.</param>
/// <param name="Kind">The 64-bit integer kind of the result.</param>
/// <param name="Mode">How an out-of-range result is reduced.</param>
public sealed record IrIntegerOverflow(IrExpression Value, IrNumericKind Kind, IrOverflowMode Mode)
    : IrExpression;

/// <summary>How <see cref="IrIntegerOverflow"/> reduces an out-of-range result.</summary>
public enum IrOverflowMode
{
    /// <summary>An unchecked context: the result wraps modulo 2^64.</summary>
    Wrap,

    /// <summary>A checked context: the result throws <c>OverflowException</c>.</summary>
    Check,
}
