using System.Globalization;
using Metano.Compiler.IR;

namespace Metano.Compiler.Dart;

/// <summary>
/// The Dart text of numeric literals. Dart has no unsigned type, so a
/// <c>ulong</c> above <c>long.MaxValue</c> prints as the <c>int</c> with the
/// same 64 bits, and a literal that prints with a leading minus binds like a
/// prefix operator (<c>-1.toString()</c> negates the call, <c>--1</c> is a
/// decrement), so it is grouped where a prefix operator would be.
/// </summary>
internal static class DartNumericLiterals
{
    /// <summary>
    /// The invariant-culture text of a numeric <paramref name="literal"/>; a
    /// <c>ulong</c> prints as the Dart <c>int</c> with the same 64 bits.
    /// </summary>
    public static string TextOf(IrLiteral literal) =>
        literal.Value switch
        {
            ulong unsigned when literal.Kind is IrLiteralKind.UInt64 => SameBitsAsInt64(unsigned)
                .ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other?.ToString() ?? "null",
        };

    /// <summary>Whether <paramref name="expression"/> is a numeric literal printed with a leading minus.</summary>
    public static bool IsNegative(IrExpression expression) =>
        expression
            is IrLiteral
            {
                Kind: IrLiteralKind.Int32
                    or IrLiteralKind.Int64
                    or IrLiteralKind.UInt64
                    or IrLiteralKind.Float64
            } literal
        && TextOf(literal).StartsWith('-');

    /// <summary>The two's-complement <c>long</c> with the bits of <paramref name="value"/>.</summary>
    private static long SameBitsAsInt64(ulong value) => unchecked((long)value);
}
