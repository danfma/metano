namespace Metano.Compiler.IR;

/// <summary>
/// A C# numeric conversion whose source or target is a 64-bit integer,
/// implicit (<c>long x = i</c>) or explicit (<c>(int)l</c>). The core decides
/// it from Roslyn's semantic model, so a target renders it from
/// <see cref="From"/>, <see cref="To"/> and <see cref="IsChecked"/> alone,
/// without re-deriving operand types.
/// </summary>
/// <param name="Operand">The converted value.</param>
/// <param name="From">The exact numeric kind of <paramref name="Operand"/>; an
/// enum operand reports its underlying kind and sets <paramref name="FromEnum"/>.</param>
/// <param name="To">The exact numeric kind of the result; an enum target reports
/// its underlying kind and sets <paramref name="ToEnum"/>.</param>
/// <param name="IsChecked">Roslyn's <c>IConversionOperation.IsChecked</c>: the
/// conversion throws <c>OverflowException</c> when the value does not fit.</param>
/// <param name="FromEnum">Whether the source is an enum value.</param>
/// <param name="ToEnum">Whether the target is an enum type.</param>
/// <param name="IsLifted">Whether the conversion is lifted over nullable values
/// (<c>int?</c> to <c>long?</c>), so <c>null</c> passes through unchanged.</param>
public sealed record IrNumericConversion(
    IrExpression Operand,
    IrNumericKind From,
    IrNumericKind To,
    bool IsChecked,
    bool FromEnum = false,
    bool ToEnum = false,
    bool IsLifted = false
) : IrExpression;

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
