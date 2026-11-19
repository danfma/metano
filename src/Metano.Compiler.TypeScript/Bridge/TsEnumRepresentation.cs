using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// How TypeScript holds a numeric enum's values: a <c>number</c>, or a
/// <c>bigint</c> for an enum over <c>long</c> / <c>ulong</c> (spec 011 FR-044
/// as amended by #701). The one predicate every TypeScript emission of an
/// enum value asks.
/// </summary>
internal static class TsEnumRepresentation
{
    /// <summary>Whether the enum <paramref name="type"/> (or its nullable form) holds <c>bigint</c>s.</summary>
    public static bool IsBigInt(IrTypeRef type) =>
        (type is IrNullableTypeRef nullable ? nullable.Inner : type)
            is IrNamedTypeRef { Semantics.EnumUnderlyingKind: { } underlying }
        && underlying.IsSixtyFourBit();

    /// <summary>Whether the enum <paramref name="declaration"/> holds <c>bigint</c>s.</summary>
    public static bool IsBigInt(IrEnumDeclaration declaration) =>
        declaration.UnderlyingKind.IsSixtyFourBit();

    /// <summary>Whether the Roslyn enum <paramref name="type"/> holds <c>bigint</c>s.</summary>
    public static bool IsBigInt(ITypeSymbol type) =>
        type is INamedTypeSymbol { EnumUnderlyingType.SpecialType: var special }
        && special is SpecialType.System_Int64 or SpecialType.System_UInt64;

    /// <summary>
    /// The conversion endpoint as TypeScript holds it: every numeric enum
    /// converts as its underlying integer (#701, #708).
    /// </summary>
    public static IrNumericEndpoint Of(IrNumericEndpoint endpoint) =>
        endpoint.AsUnderlyingInteger();

    /// <summary>
    /// An integer constant of the enum <paramref name="enumType"/> (a constant
    /// <c>0</c> C# converts to it, the zero a missing member defaults to) in
    /// the enum's representation: a 64-bit literal (<c>0n</c>) for a 64-bit
    /// enum, unchanged otherwise.
    /// </summary>
    public static IrLiteral Constant(IrLiteral literal, IrTypeRef enumType) =>
        literal.Kind is IrLiteralKind.Int32 && IsBigInt(enumType)
            ? literal with
            {
                Value = Convert.ToInt64(literal.Value, CultureInfo.InvariantCulture),
                Kind = IrLiteralKind.Int64,
            }
            : literal;
}
