using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Classifies Roslyn types as the <see cref="IrNumericEndpoint"/>s of a
/// numeric conversion (spec 012).
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
        var underlying = UnwrapNullable(type);

        if (underlying is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            return KindOf(enumType.EnumUnderlyingType) is { } enumKind
                ? new IrNumericEndpoint(enumKind, IsEnum: true)
                : null;

        return KindOf(underlying) is { } kind ? new IrNumericEndpoint(kind) : null;
    }

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

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
            ? nullable.TypeArguments[0]
            : type;
}
