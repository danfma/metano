using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>checked</c> / <c>unchecked</c> contexts (spec 012 FR-023). Roslyn folds
/// the context into the <c>IsChecked</c> flag of every operation inside, so
/// the expression lowers to its operand and the statement to its block. A
/// constant expression is the exception: C# evaluates it at compile time
/// modulo its type (<c>unchecked((int)2166136261)</c> is <c>-2128831035</c>),
/// which a transparent lowering would print unbounded, so an integral
/// constant inside a checked context lowers to the value Roslyn computed.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The literal Roslyn folded for <paramref name="expression"/> when it is
    /// a constant operation (a cast, an operator or a context expression) of
    /// an integral, enum or <c>char</c> type inside a <c>checked</c> /
    /// <c>unchecked</c> context; <c>null</c> otherwise. An enum value keeps the
    /// cast a constant enum cast lowers to outside a checked context
    /// (<c>(Color)1</c>), around its folded underlying constant.
    /// </summary>
    private IrExpression? FoldedCheckedContextConstant(ExpressionSyntax expression)
    {
        if (!IsFoldableOperation(expression) || !IsInCheckedContext(expression))
            return null;

        var constant = _semantic.GetConstantValue(expression);
        var typeInfo = _semantic.GetTypeInfo(expression);

        if (!constant.HasValue || !IsFoldableTarget(typeInfo.Type))
            return null;

        var folded = ConstantLiteral(constant.Value!, typeInfo.Type, typeInfo.ConvertedType);

        return typeInfo.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            ? new IrCastExpression(folded, IrTypeRefMapper.Map(enumType, _originResolver, _target))
            : folded;
    }

    /// <summary>
    /// A cast or an operator, possibly inside a context expression or
    /// parentheses; a bare name or literal keeps its own lowering.
    /// </summary>
    private static bool IsFoldableOperation(ExpressionSyntax expression) =>
        expression switch
        {
            CheckedExpressionSyntax context => IsFoldableOperation(context.Expression),
            ParenthesizedExpressionSyntax parenthesized => IsFoldableOperation(
                parenthesized.Expression
            ),
            CastExpressionSyntax or BinaryExpressionSyntax or PrefixUnaryExpressionSyntax => true,
            _ => false,
        };

    /// <summary>
    /// Whether <paramref name="expression"/> is, or sits inside, a
    /// <c>checked</c> / <c>unchecked</c> expression or statement of its member.
    /// </summary>
    private static bool IsInCheckedContext(ExpressionSyntax expression) =>
        CheckedContexts.Innermost(expression) is not null;

    /// <summary>
    /// An integral type, an enum over one, or <c>char</c>: the types whose
    /// constants C# wraps modulo their range.
    /// </summary>
    private static bool IsFoldableTarget(ITypeSymbol? type) =>
        type switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: var underlying } =>
                IsIntegral(underlying),
            { SpecialType: SpecialType.System_Char } => true,
            _ => IsIntegral(type),
        };

    private static bool IsIntegral(ITypeSymbol? type) =>
        type?.SpecialType
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64;

    /// <summary>
    /// The types narrower than <c>int</c> carry their value as an <c>int</c>,
    /// as their literals do.
    /// </summary>
    private static object WidenedToInt32(object value) =>
        value is sbyte or byte or short or ushort ? Convert.ToInt32(value) : value;
}
