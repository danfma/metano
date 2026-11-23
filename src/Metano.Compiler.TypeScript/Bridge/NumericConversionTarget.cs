using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// C#'s better conversion target between two numeric parameter types
/// (ECMA-334 §12.6.4.7), for ordering the arms of an overload dispatcher: a
/// value both guards accept goes to the overload C# binds a constant of that
/// value to (<c>F(5)</c> picks <c>F(int)</c> over <c>F(long)</c>,
/// <c>F(uint)</c> or <c>F(double)</c>, and <c>F(sbyte)</c> over
/// <c>F(byte)</c>).
/// </summary>
internal static class NumericConversionTarget
{
    /// <summary>
    /// Whether <paramref name="candidate"/> is a better conversion target than
    /// <paramref name="other"/>: it converts implicitly to the other and not
    /// back, or it is a signed integer and the other an unsigned one at least
    /// as wide.
    /// </summary>
    public static bool IsBetter(IrNumericKind candidate, IrNumericKind other)
    {
        if (ConvertsImplicitly(candidate, other) && !ConvertsImplicitly(other, candidate))
            return true;

        return candidate.IsSigned()
            && other.IsInteger()
            && !other.IsSigned()
            && other.BitWidth() >= candidate.BitWidth();
    }

    /// <summary>
    /// The numeric kind of a dispatched parameter type: an integer, <c>float</c>
    /// or <c>double</c> primitive, with the C# spelling a folded primitive keeps
    /// in <see cref="IrPrimitiveTypeRef.ExactKind"/>; <c>null</c> otherwise.
    /// </summary>
    public static IrNumericKind? KindOf(IrTypeRef type) =>
        type is IrPrimitiveTypeRef primitive
            ? primitive.ExactKind ?? KindOfPrimitive(primitive.Primitive)
            : null;

    private static IrNumericKind? KindOfPrimitive(IrPrimitive primitive) =>
        primitive switch
        {
            IrPrimitive.Byte => IrNumericKind.Byte,
            IrPrimitive.Int16 => IrNumericKind.Int16,
            IrPrimitive.Int32 => IrNumericKind.Int32,
            IrPrimitive.Int64 => IrNumericKind.Int64,
            IrPrimitive.UInt64 => IrNumericKind.UInt64,
            IrPrimitive.Float32 => IrNumericKind.Single,
            IrPrimitive.Float64 => IrNumericKind.Double,
            _ => null,
        };

    /// <summary>
    /// C#'s implicit numeric conversion between two distinct kinds: an integer
    /// to an integer that holds every value of it, any integer to
    /// <c>float</c> or <c>double</c>, and <c>float</c> to <c>double</c>.
    /// </summary>
    private static bool ConvertsImplicitly(IrNumericKind source, IrNumericKind target)
    {
        if (source == target)
            return false;

        if (target is IrNumericKind.Double)
            return source.IsInteger() || source is IrNumericKind.Single;

        if (target is IrNumericKind.Single)
            return source.IsInteger();

        return target.HoldsEvery(source);
    }
}
