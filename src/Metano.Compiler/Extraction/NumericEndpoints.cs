using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Classifies Roslyn types as the <see cref="IrNumericEndpoint"/>s of a
/// numeric conversion (spec 012 FR-010), and decides which conversions the
/// core makes explicit as an <see cref="IrNumericConversion"/>.
/// </summary>
internal static class NumericEndpoints
{
    /// <summary>
    /// The endpoint of <paramref name="type"/>, looking through
    /// <c>Nullable&lt;T&gt;</c>: its exact numeric kind, or an enum's
    /// underlying kind with <see cref="IrNumericEndpoint.IsEnum"/> set;
    /// <c>null</c> for a non-numeric type.
    /// </summary>
    public static IrNumericEndpoint? Of(ITypeSymbol? type)
    {
        if (type is null)
            return null;

        var underlying = type.UnwrapNullableValueType();

        if (underlying is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            return KindOf(enumType.EnumUnderlyingType) is { } enumKind
                ? new IrNumericEndpoint(enumKind, IsEnum: true)
                : null;

        return KindOf(underlying) is { } kind ? new IrNumericEndpoint(kind) : null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> (or its nullable underlying type) is a
    /// 64-bit integer: <c>long</c>, <c>ulong</c>, <c>nint</c> or <c>nuint</c>.
    /// </summary>
    public static bool IsSixtyFourBit(ITypeSymbol? type) =>
        Of(type) is { } endpoint && endpoint.IsSixtyFourBitValue();

    /// <summary>
    /// Whether the conversion from <paramref name="from"/> to
    /// <paramref name="to"/> is made explicit for its 64-bit side: one side
    /// is a 64-bit integer or a <c>BigInteger</c> (spec 012 FR-003), so one
    /// table decides every <c>bigint</c> conversion.
    /// </summary>
    public static bool NeedsExactConversion(IrNumericEndpoint from, IrNumericEndpoint to) =>
        from != to && (from.IsWideIntegerValue() || to.IsWideIntegerValue());

    /// <summary>
    /// Whether a conversion from <paramref name="from"/> to
    /// <paramref name="to"/> becomes an <see cref="IrNumericConversion"/>: a
    /// floating-point truncation (FR-070), and the conversions of
    /// <see cref="NeedsExactConversion"/>.
    /// </summary>
    public static bool IsLoweredExplicitly(IrNumericEndpoint from, IrNumericEndpoint to) =>
        IrNumericConversion.IsFloatingPointTruncation(from, to) || NeedsExactConversion(from, to);

    private static IrNumericKind? KindOf(ITypeSymbol? type) =>
        type?.SpecialType switch
        {
            SpecialType.System_SByte => IrNumericKind.SByte,
            SpecialType.System_Byte => IrNumericKind.Byte,
            SpecialType.System_Int16 => IrNumericKind.Int16,
            SpecialType.System_UInt16 => IrNumericKind.UInt16,
            SpecialType.System_Int32 => IrNumericKind.Int32,
            SpecialType.System_UInt32 => IrNumericKind.UInt32,
            SpecialType.System_Int64 or SpecialType.System_IntPtr => IrNumericKind.Int64,
            SpecialType.System_UInt64 or SpecialType.System_UIntPtr => IrNumericKind.UInt64,
            SpecialType.System_Char => IrNumericKind.Char,
            SpecialType.System_Single => IrNumericKind.Single,
            SpecialType.System_Double => IrNumericKind.Double,
            SpecialType.System_Decimal => IrNumericKind.Decimal,
            _ when type?.ToDisplayString() == "System.Numerics.BigInteger" =>
                IrNumericKind.BigInteger,
            _ => null,
        };
}
