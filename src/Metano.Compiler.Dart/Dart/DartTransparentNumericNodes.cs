using Metano.Compiler.IR;

namespace Metano.Compiler.Dart;

/// <summary>
/// The numeric IR nodes Dart prints as their operand, because a Dart
/// <c>int</c> is already a 64-bit two's-complement value:
/// <see cref="IrIntegerOverflow"/> (the VM <c>int</c> wraps by itself, and a
/// checked context is a documented gap) and <see cref="IrNumericConversion"/>
/// (the narrowing <c>.toSigned</c> / <c>.toUnsigned</c> forms land with the
/// slice that produces the node). The printer and the operand-grouping rules
/// look through them, so a wrapped operand keeps the parentheses its own
/// shape needs.
/// </summary>
internal static class DartTransparentNumericNodes
{
    /// <summary>The expression Dart actually prints for <paramref name="expression"/>.</summary>
    public static IrExpression Unwrap(IrExpression expression) =>
        expression switch
        {
            IrIntegerOverflow overflow => Unwrap(overflow.Value),
            IrNumericConversion conversion => Unwrap(conversion.Operand),
            _ => expression,
        };
}
