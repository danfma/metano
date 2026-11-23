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
        var type = _semantic.GetTypeInfo(expression).Type;
        var convertedType = ImplicitlyConvertedType(expression);

        if (!constant.HasValue || !IsNumericConstant(constant.Value))
            return null;

        var touchesSixtyFourBits =
            NumericEndpoints.IsSixtyFourBit(type) || NumericEndpoints.IsSixtyFourBit(convertedType);

        if (!touchesSixtyFourBits || IsUnconvertedDeclaredConstant(expression, type, convertedType))
            return null;

        return ConstantLiteral(constant.Value!, type, convertedType);
    }

    /// <summary>
    /// The type <paramref name="expression"/> is implicitly converted to, or
    /// its own type when Roslyn reports an explicit conversion for it: an
    /// element of a cast tuple literal (<c>((long, long))(ulong.MaxValue, x)</c>)
    /// carries the cast's element type, but its conversion is the explicit
    /// one its element extraction applies (<see cref="ConvertedLiteralElement"/>),
    /// so its constant folds in its own type (#640).
    /// </summary>
    private ITypeSymbol? ImplicitlyConvertedType(ExpressionSyntax expression)
    {
        var typeInfo = _semantic.GetTypeInfo(expression);

        return _semantic.GetConversion(expression).IsImplicit
            ? typeInfo.ConvertedType
            : typeInfo.Type;
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
        var typeInfo = _semantic.GetTypeInfo(expression);

        if (CharLiteralAsFloatingPoint(value, typeInfo) is { } code)
            return code;

        if (CarriesNoImplicitConversion(expression, value))
            return value;

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
    /// A <c>char</c> literal read as a <c>double</c> or <c>float</c>
    /// (<c>double d = 'a'</c>): its UTF-16 code as a number literal; <c>null</c>
    /// for any other value.
    /// </summary>
    private static IrLiteral? CharLiteralAsFloatingPoint(IrExpression value, TypeInfo typeInfo) =>
        value is IrLiteral { Kind: IrLiteralKind.Char, Value: char character }
        && typeInfo.ConvertedType?.SpecialType
            is SpecialType.System_Double
                or SpecialType.System_Single
            ? new IrLiteral((double)character, IrLiteralKind.Float64)
            : null;

    /// <summary>
    /// The left operand of <c>value ?? fallback</c> with the numeric conversion
    /// C# applies to it (<c>ICoalesceOperation.ValueConversion</c>), which
    /// Roslyn reports on the operation rather than on the operand: a
    /// <c>long?</c> read as the <c>double</c> result (<c>k ?? 1.5</c>) is
    /// converted before the fallback applies. Any other operand is unchanged.
    /// </summary>
    private OperatorOperand LeftOperandOf(BinaryExpressionSyntax binary)
    {
        var operand = OperandOf(binary.Left);

        if (
            !binary.IsKind(SyntaxKind.CoalesceExpression)
            || _semantic.GetOperation(binary)
                is not ICoalesceOperation
                {
                    Value.Type: { } valueType,
                    Type: { } resultType,
                } coalesce
        )
            return operand;

        if (UserCoalescedValue(coalesce, operand.Value) is { } userConverted)
            return operand.WithValue(userConverted);

        if (
            NumericEndpoints.Of(valueType.UnwrapNullableValueType()) is not { } from
            || NumericEndpoints.Of(resultType.UnwrapNullableValueType()) is not { } to
            || !_context.NumericLowering.NeedsImplicitNode(from, to)
        )
            return operand;

        var converted = new IrNumericConversion(
            operand.Value,
            from,
            to,
            IsChecked: false,
            IsLifted: valueType.IsNullableValueType()
        );

        return operand with
        {
            Value = converted,
        };
    }

    /// <summary>
    /// A literal already carries its converted kind (a <c>char</c> literal read
    /// as a number does not: the target folds it, FR-071); a parenthesized,
    /// <c>checked</c> / <c>unchecked</c> or null-forgiving (<c>x!</c>)
    /// expression leaves the conversion to its operand.
    /// </summary>
    private static bool CarriesNoImplicitConversion(
        ExpressionSyntax expression,
        IrExpression value
    ) =>
        value is IrLiteral { Kind: not IrLiteralKind.Char }
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
            _context.NumericLowering.NeedsImplicitNode(from, to)
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
    /// to a 32-bit or narrower integer (FR-070), a narrowing between two
    /// integers of 32 bits or fewer that can change the value (FR-074), and
    /// every cast into or out of a 64-bit integer or a <c>BigInteger</c>.
    /// <c>null</c> when the cast keeps its existing lowering.
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

        if (
            !_context.NumericLowering.IsLoweredExplicitly(from, to)
            || KeepsIntegerValue(cast, from, to)
        )
            return null;

        return new IrNumericConversion(
            operand,
            from,
            to,
            IsCheckedCast(cast),
            IsLifted: sourceType.IsNullableValueType(),
            CastType: castType
        );
    }

    /// <summary>
    /// Whether the explicit conversion of <paramref name="cast"/> is checked.
    /// Roslyn reports <c>IsChecked</c> for a numeric conversion only, so a
    /// conversion of a nullable value (<c>(byte?)ni</c>, <c>(int)nl</c>) and an
    /// enumeration conversion (<c>(int)e</c>, <c>(E)l</c>), which C# checks
    /// through the conversion of the underlying integers (#701), read the
    /// context of their syntax. Only a cast that lowers to a conversion node
    /// carries it, including enum narrowing of 32 bits or fewer (#708).
    /// </summary>
    private bool IsCheckedCast(CastExpressionSyntax cast) =>
        _semantic.GetOperation(cast) switch
        {
            IConversionOperation { IsChecked: true } => true,
            IConversionOperation { Conversion.IsNullable: true } => CheckedContexts.IsChecked(
                cast,
                _semantic.Compilation
            ),
            IConversionOperation conversion when IsEnumerationConversion(conversion) =>
                CheckedContexts.IsChecked(cast, _semantic.Compilation),
            _ => false,
        };

    /// <summary>
    /// A narrowing between two integers of 32 bits or fewer whose operand
    /// can only produce values the target holds (<c>(byte)(x &amp; 0xFF)</c>):
    /// the cast cannot change the value, so it lowers to nothing (FR-074).
    /// </summary>
    private bool KeepsIntegerValue(
        CastExpressionSyntax cast,
        IrNumericEndpoint from,
        IrNumericEndpoint to
    ) =>
        IrNumericConversion.IsIntegerNarrowing(
            _context.NumericLowering.RepresentationOf(from),
            _context.NumericLowering.RepresentationOf(to)
        )
        && IntegerValueRanges.Of(
            _semantic.GetOperation(Unparenthesized(cast.Expression)),
            _context.NumericLowering
        )
            is { } operand
        && IntegerRange.Of(to.Kind).Contains(operand);

    /// <summary>
    /// A constant cast that lowers to a conversion (<c>(int)3.7</c>) folds to
    /// the value Roslyn computed, as every target's conversion of the literal
    /// would (FR-070); a constant cast to any numeric enum the target represents
    /// as its underlying integer (<c>(ByteEnum)(-1)</c> unchecked, #708;
    /// <c>(LongEnum)5</c>, #701) keeps the enum assertion around that folded value.
    /// </summary>
    private IrExpression? FoldedConstantCast(CastExpressionSyntax cast)
    {
        var constant = _semantic.GetConstantValue(cast);

        if (!constant.HasValue || !IsNumericConstant(constant.Value))
            return null;

        var typeInfo = _semantic.GetTypeInfo(cast);
        var folded = ConstantLiteral(constant.Value!, typeInfo.Type, typeInfo.ConvertedType);

        return
            _context.NumericLowering.RepresentationOf(NumericEndpoints.Of(typeInfo.Type))
                is { IsEnum: false }
            ? AsEnumConstant(folded, typeInfo.Type)
            : folded;
    }

    /// <summary>
    /// A folded constant of an enum type, cast to the enum around its
    /// underlying constant (<c>(Color)1</c>); any other constant as it is.
    /// </summary>
    private IrExpression AsEnumConstant(IrLiteral folded, ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            ? new IrCastExpression(folded, IrTypeRefMapper.Map(enumType, _originResolver, _target))
            : folded;

    /// <summary>Whether <paramref name="conversion"/> converts to or from an enum.</summary>
    private static bool IsEnumerationConversion(IConversionOperation conversion) =>
        IsEnumTyped(conversion.Type) || IsEnumTyped(conversion.Operand.Type);

    private static bool IsEnumTyped(ITypeSymbol? type) =>
        NumericEndpoints.Of(type) is { IsEnum: true };

    /// <summary>
    /// The literal of a folded constant <paramref name="value"/>, in the kind
    /// of the type it is converted to (its own type when the conversion
    /// leaves the numeric types, as boxing to <c>object</c> does): a
    /// <c>char</c> read as a <c>char</c> is a character literal; <c>decimal</c>
    /// and <c>BigInteger</c> carry their digits, as their literals do; an enum
    /// value and the types narrower than <c>int</c> carry a plain integer,
    /// except a 64-bit enum the target holds as its integer, whose constant is
    /// a 64-bit literal (#701).
    /// </summary>
    private IrLiteral ConstantLiteral(
        object value,
        ITypeSymbol? type,
        ITypeSymbol? convertedType
    ) =>
        ConstantLiteralOfKind(
            value,
            _context.NumericLowering.RepresentationOf(
                NumericEndpoints.Of(convertedType) ?? NumericEndpoints.Of(type)
            )
        );

    /// <summary>The literal of a constant <paramref name="value"/> in the kind of <paramref name="target"/>.</summary>
    private static IrLiteral ConstantLiteralOfKind(object value, IrNumericEndpoint? target)
    {
        var number = value is char character ? (int)character : value;

        // An enum value is a plain number, unless the target holds it as its
        // underlying integer (ConstantLiteral reads that representation).
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
    private bool IsUnconvertedDeclaredConstant(
        ExpressionSyntax expression,
        ITypeSymbol? type,
        ITypeSymbol? convertedType
    )
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
            && NumericEndpoints.Of(type) == NumericEndpoints.Of(convertedType);
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
