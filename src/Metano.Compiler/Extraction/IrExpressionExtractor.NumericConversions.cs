using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Numeric conversions of spec 012: a floating-point value cast to a narrow
/// integer (FR-070). Roslyn decides it: the explicit conversion of the cast
/// (<see cref="IConversionOperation"/>, whose <c>IsChecked</c> carries the
/// context) and the constant value it folds. The targets render the resulting <see cref="IrNumericConversion"/>
/// without re-deriving any type.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The conversion an explicit cast from <paramref name="sourceType"/> to
    /// <paramref name="targetType"/> lowers to: a floating-point value cast
    /// to a 32-bit or narrower integer (FR-070); <c>null</c> when the cast
    /// keeps its existing lowering.
    /// </summary>
    private IrNumericConversion? ExplicitNumericConversion(
        CastExpressionSyntax cast,
        IrExpression operand,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        IrTypeRef castType
    )
    {
        if (
            NumericEndpoints.Of(sourceType) is not { } from
            || NumericEndpoints.Of(targetType) is not { } to
        )
            return null;

        if (!IrNumericConversion.IsFloatingPointTruncation(from, to))
            return null;

        var isChecked = _semantic.GetOperation(cast) is IConversionOperation { IsChecked: true };

        return new IrNumericConversion(
            operand,
            from,
            to,
            isChecked,
            IsLifted: sourceType.IsNullableValueType(),
            CastType: castType
        );
    }

    /// <summary>
    /// A constant cast that lowers to a conversion (<c>(int)3.7</c>) folds to
    /// the value Roslyn computed, as every target's conversion of the literal
    /// would (FR-070).
    /// </summary>
    private IrLiteral? FoldedConstantCast(CastExpressionSyntax cast)
    {
        var constant = _semantic.GetConstantValue(cast);

        if (!constant.HasValue || !IsNumericConstant(constant.Value))
            return null;

        var typeInfo = _semantic.GetTypeInfo(cast);

        return ConstantLiteral(constant.Value!, typeInfo.Type, typeInfo.ConvertedType);
    }

    /// <summary>
    /// The literal of a folded constant <paramref name="value"/>, in the kind
    /// of the type it is converted to (its own type when the conversion
    /// leaves the numeric types, as boxing to <c>object</c> does): a
    /// <c>char</c> read as a <c>char</c> is a character literal; <c>decimal</c>
    /// and <c>BigInteger</c> carry their digits, as their literals do; an enum
    /// value and the types narrower than <c>int</c> carry a plain integer.
    /// </summary>
    private static IrLiteral ConstantLiteral(
        object value,
        ITypeSymbol? type,
        ITypeSymbol? convertedType
    )
    {
        var target = NumericEndpoints.Of(convertedType) ?? NumericEndpoints.Of(type);
        var number = value is char character ? (int)character : value;

        // An enum value is a plain number in every target, whatever its
        // underlying kind (DECIDED-15).
        if (target is { IsEnum: true })
            return new IrLiteral(WidenedToInt32(number), IrLiteralKind.Int32);

        return target?.Kind switch
        {
            IrNumericKind.Char => new IrLiteral(Convert.ToChar(number), IrLiteralKind.Char),
            IrNumericKind.Decimal => new IrLiteral(Digits(number), IrLiteralKind.Decimal),
            IrNumericKind.BigInteger => new IrLiteral(Digits(number), IrLiteralKind.BigInteger),
            IrNumericKind.Int64 => new IrLiteral(Convert.ToInt64(number), IrLiteralKind.Int64),
            IrNumericKind.UInt64 => new IrLiteral(Convert.ToUInt64(number), IrLiteralKind.UInt64),
            IrNumericKind.Double => new IrLiteral(Convert.ToDouble(number), IrLiteralKind.Float64),
            IrNumericKind.Single => new IrLiteral(
                (double)Convert.ToSingle(number),
                IrLiteralKind.Float64
            ),
            _ => ClassifyNumeric(WidenedToInt32(number)),
        };
    }

    private static string Digits(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture)!;

    private static bool IsNumericConstant(object? value) => value is not (null or string or bool);
}
