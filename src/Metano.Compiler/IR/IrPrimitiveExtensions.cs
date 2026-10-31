using System.Diagnostics.CodeAnalysis;

namespace Metano.Compiler.IR;

/// <summary>Classification helpers for <see cref="IrPrimitive"/> and primitive type refs.</summary>
public static class IrPrimitiveExtensions
{
    /// <summary>
    /// Whether a <c>[Branded]</c> wrapper over this primitive lowers to a
    /// branded primitive (<c>primitive &amp; { __brand }</c>) rather than a
    /// regular class: the scalar primitives that lower to a native value type
    /// (string, number, boolean, bigint). <see cref="IrPrimitive.Guid"/>,
    /// date/time types, <see cref="IrPrimitive.Object"/> and
    /// <see cref="IrPrimitive.Void"/> are excluded — a backend may lower them
    /// to a nominal or structural type that can't carry the brand.
    /// </summary>
    public static bool IsBrandable(this IrPrimitive primitive) =>
        primitive
            is IrPrimitive.Boolean
                or IrPrimitive.Byte
                or IrPrimitive.Int16
                or IrPrimitive.Int32
                or IrPrimitive.Int64
                or IrPrimitive.Float32
                or IrPrimitive.Float64
                or IrPrimitive.Decimal
                or IrPrimitive.BigInteger
                or IrPrimitive.String
                or IrPrimitive.Char;

    /// <summary>
    /// Whether <paramref name="type"/> is a non-nullable primitive that a
    /// <c>[Branded]</c> wrapper can brand (see <see cref="IsBrandable"/>).
    /// The extractor (value-read collapse) and each backend's wrapper bridge
    /// consult this one rule so they agree on which wrappers are branded.
    /// </summary>
    public static bool IsBrandablePrimitive([NotNullWhen(true)] this IrTypeRef? type) =>
        type is IrPrimitiveTypeRef { Primitive: var primitive } && primitive.IsBrandable();
}
