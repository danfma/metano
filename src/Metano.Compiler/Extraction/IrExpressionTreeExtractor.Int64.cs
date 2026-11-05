using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The 64-bit side of the expression-tree walker (spec 012 FR-044), under
/// the exact 64-bit lowering only: a cast into or out of a 64-bit integer,
/// and an implicit numeric conversion Roslyn reports into or out of one,
/// become an <see cref="IrExprConvert"/>; a constant converted to a 64-bit
/// integer folds to a literal holding the converted value.
/// </summary>
internal sealed partial class IrExpressionTreeExtractor
{
    private bool IsSixtyFourBitConversion(CastExpressionSyntax cast) =>
        _exactInt64Lowering
        && _semantic.GetOperation(cast)
            is IConversionOperation { Conversion.IsNumeric: true } conversion
        && NeedsConvertNode(conversion.Operand.Type, conversion.Type);

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
    /// reports for <paramref name="syntax"/> when it crosses a 64-bit integer.
    /// A parenthesized expression's operand carries the conversion, so the
    /// parentheses are not wrapped twice.
    /// </summary>
    private IrExprTreeNode? WithImplicitSixtyFourBitConversion(
        ExpressionSyntax syntax,
        IrExprTreeNode? node
    )
    {
        if (node is null || !_exactInt64Lowering || syntax is ParenthesizedExpressionSyntax)
            return node;

        var conversion = _semantic.GetConversion(syntax);
        var typeInfo = _semantic.GetTypeInfo(syntax);

        if (!conversion.IsImplicit || !conversion.IsNumeric)
            return node;

        if (!NeedsConvertNode(typeInfo.Type, typeInfo.ConvertedType))
            return node;

        var target = MapTypeSymbol(typeInfo.ConvertedType)!;

        return node is IrExprLiteral { Value: { } value }
            ? new IrExprLiteral(ConvertedConstant(value, typeInfo.ConvertedType!), target)
            : new IrExprConvert(node, target);
    }

    private bool ConvertsAcrossSixtyFourBits(TypeInfo info) =>
        _exactInt64Lowering && NeedsConvertNode(info.Type, info.ConvertedType);

    /// <summary>
    /// A capture's value without the 64-bit conversion the value extractor
    /// applies in its position: the tree converts the capture itself.
    /// </summary>
    private IrExpression WithoutSixtyFourBitConversion(IrExpression value) =>
        _exactInt64Lowering && value is IrNumericConversion { IsLifted: false } conversion
            ? conversion.Operand
            : value;

    private static bool NeedsConvertNode(ITypeSymbol? from, ITypeSymbol? to) =>
        NumericEndpoints.Of(from) is { } source
        && NumericEndpoints.Of(to) is { } target
        && (source.IsSixtyFourBitValue() || target.IsSixtyFourBitValue())
        && NumericEndpoints.NeedsExactConversion(source, target);

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
