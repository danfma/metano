using System.Globalization;

namespace Metano.Compiler.IR;

/// <summary>Target-agnostic C# facts of a built-in numeric <see cref="IrBinaryExpression"/>.</summary>
public static class IrBinaryExpressionExtensions
{
    /// <summary>
    /// Whether an integer division or remainder cannot throw: its divisor is
    /// a constant other than <c>0</c>, and other than <c>-1</c> for a signed
    /// operand kind (<c>long.MinValue / -1</c> throws <c>OverflowException</c>,
    /// spec 012 DECIDED-8). It reads the literal the core folds every
    /// constant 64-bit operand into (FR-008), so a constant divisor never
    /// arrives as a conversion node. Callers pass an integer division or a
    /// remainder.
    /// </summary>
    public static bool HasNonThrowingConstantDivisor(this IrBinaryExpression division) =>
        division.Right is IrLiteral { Value: long or ulong or int } divisor
        && Convert.ToDecimal(divisor.Value, CultureInfo.InvariantCulture) switch
        {
            0m => false,
            -1m => division.NumericKind?.IsSigned() == false,
            _ => true,
        };
}
