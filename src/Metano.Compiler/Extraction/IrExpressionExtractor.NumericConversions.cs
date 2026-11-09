using System.Globalization;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Numeric conversions and constants of the 64-bit lowering (spec 012
/// FR-008, FR-010, FR-070). Roslyn decides every case: the implicit
/// conversion it reports for an expression (<c>GetConversion</c> and the
/// converted type), the explicit one of a cast (<see cref="IConversionOperation"/>,
/// whose <c>IsChecked</c> carries the context) and the constant value it
/// folds. The targets render the resulting <see cref="IrNumericConversion"/>
/// without re-deriving any type.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The literal a 64-bit constant folds to (FR-008): any expression whose
    /// own or converted type is a 64-bit integer and that Roslyn reports as a
    /// constant (<c>long.MaxValue</c>, <c>1L &lt;&lt; 40</c>, <c>5</c> in a
    /// <c>long</c> slot), plus the native-integer limits, which are not C#
    /// constants (FR-005). A bare reference to a constant declared in source
    /// stays a reference when it needs no conversion. <c>null</c> otherwise.
    /// </summary>
    private IrLiteral? FoldedInt64Constant(ExpressionSyntax expression)
    {
        if (NativeIntegerLimit(expression) is { } limit)
            return limit;

        var constant = _semantic.GetConstantValue(expression);
        var typeInfo = _semantic.GetTypeInfo(expression);

        if (!constant.HasValue || !IsNumericConstant(constant.Value))
            return null;

        var touchesSixtyFourBits =
            NumericEndpoints.IsSixtyFourBit(typeInfo.Type)
            || NumericEndpoints.IsSixtyFourBit(typeInfo.ConvertedType);

        if (!touchesSixtyFourBits || IsUnconvertedDeclaredConstant(expression, typeInfo))
            return null;

        return ConstantLiteral(constant.Value!, typeInfo.Type, typeInfo.ConvertedType);
    }

    /// <summary>
    /// Wraps <paramref name="value"/> in the implicit numeric conversion
    /// Roslyn reports for <paramref name="expression"/> when it crosses a
    /// 64-bit integer (FR-010, FR-019). A parenthesized or
    /// <c>checked</c> / <c>unchecked</c> expression reports the conversion of
    /// its operand, which carries it, so it is not wrapped twice. A relational
    /// operand of two integer kinds keeps its own type, because JS compares a
    /// <c>bigint</c> and an integral <c>number</c> exactly (FR-069).
    /// </summary>
    private IrExpression WithImplicitNumericConversion(
        ExpressionSyntax expression,
        IrExpression value
    )
    {
        if (CarriesNoImplicitConversion(expression, value))
            return value;

        var typeInfo = _semantic.GetTypeInfo(expression);

        if (ImplicitSixtyFourBitEndpoints(expression, typeInfo) is not var (from, to))
            return value;

        return new IrNumericConversion(
            value,
            from,
            to,
            IsChecked: false,
            IsLifted: typeInfo.Type.IsNullableValueType()
        );
    }

    /// <summary>
    /// A literal already carries its converted kind; a parenthesized,
    /// <c>checked</c> / <c>unchecked</c> or null-forgiving (<c>x!</c>)
    /// expression leaves the conversion to its operand.
    /// </summary>
    private static bool CarriesNoImplicitConversion(
        ExpressionSyntax expression,
        IrExpression value
    ) =>
        value is IrLiteral
        || expression is ParenthesizedExpressionSyntax or CheckedExpressionSyntax
        || expression.IsKind(SyntaxKind.SuppressNullableWarningExpression);

    /// <summary>
    /// The endpoints of the implicit conversion Roslyn reports for
    /// <paramref name="expression"/>, when the 64-bit lowering makes it
    /// explicit; <c>null</c> for no conversion, a non-numeric one, one that
    /// needs no node, or an exact relational operand (FR-069).
    /// </summary>
    private (IrNumericEndpoint From, IrNumericEndpoint To)? ImplicitSixtyFourBitEndpoints(
        ExpressionSyntax expression,
        TypeInfo typeInfo
    )
    {
        var conversion = _semantic.GetConversion(expression);

        if (!conversion.IsImplicit || conversion.IsIdentity)
            return null;

        if (
            NumericEndpoints.Of(typeInfo.Type) is not { } from
            || NumericEndpoints.Of(typeInfo.ConvertedType) is not { } to
        )
            return null;

        var needsNode =
            NumericEndpoints.CrossesSixtyFourBits(from, to)
            && !IsExactRelationalOperand(expression, from, to);

        return needsNode ? (from, to) : null;
    }

    /// <summary>
    /// A numeric cast that lowers to an <see cref="IrNumericConversion"/>
    /// (<see cref="ExplicitNumericConversion"/>), or its folded constant;
    /// <c>null</c> when the cast keeps its other lowerings. A cast that
    /// unwrapped a nullable value converts it as its underlying type, so a
    /// target that keeps the cast (Dart) reads it through that type.
    /// </summary>
    private IrExpression? LowerNumericCast(
        CastExpressionSyntax cast,
        IrExpression inner,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        bool unwrapsNullable
    )
    {
        var operand = unwrapsNullable
            ? new IrCastExpression(inner, IrTypeRefMapper.Map(sourceType, _originResolver, _target))
            : inner;

        var conversion = ExplicitNumericConversion(
            cast,
            operand,
            sourceType,
            targetType,
            ExtractTargetType(cast)
        );

        return conversion is null ? null : FoldedConstantCast(cast) ?? (IrExpression)conversion;
    }

    /// <summary>
    /// A cast of a nullable value type to its non-nullable value (<c>(int)ni</c>),
    /// other than a user-defined conversion, which takes the nullable itself.
    /// A user-defined operator over the non-nullable type
    /// (<c>(int)nullableBigInteger</c>) unwraps too, as C# reads
    /// <c>.Value</c> before it.
    /// </summary>
    private bool IsNullableUnwrapCast(
        CastExpressionSyntax cast,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    ) =>
        UnwrapsNullableValue(sourceType, targetType)
        && _semantic.GetOperation(cast) switch
        {
            IConversionOperation { Conversion.IsUserDefined: true } conversion =>
                TakesNonNullableOperand(conversion),
            _ => true,
        };

    private static bool TakesNonNullableOperand(IConversionOperation conversion) =>
        conversion.Conversion.MethodSymbol?.Parameters is [{ Type: var operand }]
        && !operand.IsNullableValueType();

    /// <summary>
    /// The conversion an explicit cast from <paramref name="sourceType"/> to
    /// <paramref name="targetType"/> lowers to: a floating-point value cast
    /// to a 32-bit or narrower integer (FR-070), and every cast into or out
    /// of a 64-bit integer or a <c>BigInteger</c>. <c>null</c> when the cast
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

        if (!NumericEndpoints.IsLoweredExplicitly(from, to))
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
    ) =>
        ConstantLiteralOfKind(
            value,
            NumericEndpoints.Of(convertedType) ?? NumericEndpoints.Of(type)
        );

    /// <summary>The literal of a constant <paramref name="value"/> in the kind of <paramref name="target"/>.</summary>
    private static IrLiteral ConstantLiteralOfKind(object value, IrNumericEndpoint? target)
    {
        // TODO(#602): merge ClassifyNumericForConvertedType into this.
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

    /// <summary>
    /// <c>nint.MaxValue</c> / <c>MinValue</c>, <c>nuint.MaxValue</c> /
    /// <c>MinValue</c> and <c>IntPtr.Size</c>: properties, not C# constants
    /// (CS0133), whose values are fixed on every 64-bit host Metano targets
    /// (DECIDED-2, FR-005).
    /// </summary>
    private IrLiteral? NativeIntegerLimit(ExpressionSyntax expression)
    {
        if (expression is not MemberAccessExpressionSyntax)
            return null;

        if (
            _semantic.GetSymbolInfo(expression).Symbol
            is not IPropertySymbol { IsStatic: true } property
        )
            return null;

        return (property.ContainingType.SpecialType, property.Name) switch
        {
            (SpecialType.System_IntPtr, "MaxValue") => new IrLiteral(
                long.MaxValue,
                IrLiteralKind.Int64
            ),
            (SpecialType.System_IntPtr, "MinValue") => new IrLiteral(
                long.MinValue,
                IrLiteralKind.Int64
            ),
            (SpecialType.System_UIntPtr, "MaxValue") => new IrLiteral(
                ulong.MaxValue,
                IrLiteralKind.UInt64
            ),
            (SpecialType.System_UIntPtr, "MinValue") => new IrLiteral(0UL, IrLiteralKind.UInt64),
            (SpecialType.System_IntPtr or SpecialType.System_UIntPtr, "Size") => new IrLiteral(
                8,
                IrLiteralKind.Int32
            ),
            _ => null,
        };
    }

    /// <summary>
    /// A bare reference to a constant field or local declared in source (not
    /// a BCL or referenced-assembly constant, which always folds), read as
    /// its own type: it keeps its name (FR-008). A converted read
    /// (<c>long x = IntConst;</c>) folds instead.
    /// </summary>
    private bool IsUnconvertedDeclaredConstant(ExpressionSyntax expression, TypeInfo typeInfo)
    {
        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return false;

        var isDeclaredConstant = _semantic.GetSymbolInfo(expression).Symbol switch
        {
            IFieldSymbol { IsConst: true } field => !field.DeclaringSyntaxReferences.IsEmpty,
            ILocalSymbol { IsConst: true } => true,
            _ => false,
        };

        return isDeclaredConstant
            && NumericEndpoints.Of(typeInfo.Type) == NumericEndpoints.Of(typeInfo.ConvertedType);
    }

    /// <summary>
    /// An operand of <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c>
    /// converted between two integer kinds (FR-069).
    /// </summary>
    private static bool IsExactRelationalOperand(
        ExpressionSyntax expression,
        IrNumericEndpoint from,
        IrNumericEndpoint to
    ) =>
        OutermostParenthesized(expression).Parent
            is BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LessThanExpression
                    or (int)SyntaxKind.LessThanOrEqualExpression
                    or (int)SyntaxKind.GreaterThanExpression
                    or (int)SyntaxKind.GreaterThanOrEqualExpression,
            }
        && from.Kind.IsInteger()
        && to.Kind.IsInteger();

    private static SyntaxNode OutermostParenthesized(ExpressionSyntax expression)
    {
        SyntaxNode node = expression;

        while (node.Parent is ParenthesizedExpressionSyntax parenthesized)
            node = parenthesized;

        return node;
    }
}
