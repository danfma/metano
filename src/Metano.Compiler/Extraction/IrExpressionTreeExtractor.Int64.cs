using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The 64-bit side of the expression-tree walker (spec 012 FR-044): a cast
/// into or out of a 64-bit integer, and an implicit numeric conversion Roslyn
/// reports into or out of one, become an <see cref="IrExprConvert"/>; a constant converted to a 64-bit
/// integer folds to a literal holding the converted value.
/// </summary>
internal sealed partial class IrExpressionTreeExtractor
{
    private bool IsSixtyFourBitCast(CastExpressionSyntax cast) =>
        _semantic.GetOperation(cast)
            is IConversionOperation
            {
                Conversion: { IsNumeric: true } or { IsNullable: true }
            } conversion
        && CrossesSixtyFourBits(conversion.Operand.Type, conversion.Type);

    private IrExprTreeNode? WalkSixtyFourBitCast(CastExpressionSyntax cast)
    {
        if (TryFoldConstant(cast, out var folded))
            return folded;

        var operand = Walk(cast.Expression);
        var type = MapType(cast);

        return operand is null || type is null ? null : new IrExprConvert(operand, type);
    }

    /// <summary>
    /// Wraps <paramref name="node"/> in the implicit numeric conversion Roslyn
    /// reports for <paramref name="syntax"/> when it crosses a 64-bit integer,
    /// lifted (<c>int</c> → <c>long?</c>) or not. A parenthesized
    /// expression's operand carries the conversion, so the parentheses are
    /// not wrapped twice.
    /// </summary>
    private IrExprTreeNode? WithImplicitSixtyFourBitConversion(
        ExpressionSyntax syntax,
        IrExprTreeNode? node
    )
    {
        if (node is null || syntax is ParenthesizedExpressionSyntax)
            return node;

        var typeInfo = _semantic.GetTypeInfo(syntax);

        if (!ConvertedTypeCrossesSixtyFourBits(syntax, typeInfo))
            return node;

        if (MapTypeSymbol(typeInfo.ConvertedType) is not { } target)
            return node;

        return node is IrExprLiteral { Value: { } value }
            ? new IrExprLiteral(ConvertedConstant(value, typeInfo.ConvertedType!), target)
            : new IrExprConvert(node, target);
    }

    /// <summary>
    /// Whether <paramref name="syntax"/> has an implicit numeric conversion,
    /// lifted or not, across a 64-bit integer: the conversion a
    /// <see cref="IrExprConvert"/> node carries, so the operand keeps its own
    /// type.
    /// </summary>
    private bool ConvertedTypeCrossesSixtyFourBits(ExpressionSyntax syntax, TypeInfo info)
    {
        var conversion = _semantic.GetConversion(syntax);

        return conversion.IsImplicit
            && IsNumericOrLifted(conversion)
            && CrossesSixtyFourBits(info.Type, info.ConvertedType);
    }

    /// <summary>
    /// A capture's value without the 64-bit conversion, lifted or not, the
    /// value extractor applies in its position: the tree converts the capture
    /// itself.
    /// </summary>
    private static IrExpression WithoutSixtyFourBitConversion(IrExpression value) =>
        value is IrNumericConversion conversion ? conversion.Operand : value;

    /// <summary>
    /// A numeric conversion, or a lifted one (<c>int?</c> → <c>long?</c>):
    /// <see cref="CrossesSixtyFourBits"/> then requires numeric endpoints on
    /// both sides.
    /// </summary>
    private static bool IsNumericOrLifted(Conversion conversion) =>
        conversion.IsNumeric || conversion.IsNullable;

    private static bool CrossesSixtyFourBits(ITypeSymbol? from, ITypeSymbol? to) =>
        NumericEndpoints.Of(from) is { } source
        && NumericEndpoints.Of(to) is { } target
        && (source.IsSixtyFourBitValue() || target.IsSixtyFourBitValue())
        && NumericEndpoints.CrossesSixtyFourBits(source, target);

    /// <summary>A constant's value in the type it converts to (<c>5</c> in a <c>long</c> slot is <c>5L</c>).</summary>
    private static object ConvertedConstant(object value, ITypeSymbol type) =>
        type.UnderlyingValueType()?.SpecialType switch
        {
            SpecialType.System_Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            SpecialType.System_UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
            SpecialType.System_Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            SpecialType.System_Single => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            SpecialType.System_Decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            _ => value,
        };
}
