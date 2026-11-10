using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Tuple conversions that cross a 64-bit integer (spec 012 FR-010): C#
/// converts a tuple element by element (<c>(int, int)</c> to
/// <c>(long, long)</c>, nested and nullable tuples included), and a tuple is
/// one value in the targets, so the conversion rebuilds it — each element is
/// read by position and goes through the scalar
/// <see cref="IrNumericConversion"/> its own conversion needs. A source with
/// side effects (a call) is bound to a temporary and evaluated once; a
/// property read counts as side-effect free and is not bound, so a getter
/// source is read once per element read (#630). A tuple literal is not
/// rebuilt: its elements convert where they are written, in source order —
/// implicitly as Roslyn reports them, and under an explicit cast through
/// the numeric conversion their types lower to, a missing nullable value
/// throwing there. An element whose conversion lowers to nothing
/// (<c>int?</c> to <c>int</c>, <c>long?</c> to <c>long</c>) is not unwrapped
/// the way its scalar cast is: it keeps <c>null</c> (#630). Dart keeps the
/// tuple as it is: its <c>int</c> holds both widths, so a record of them
/// needs no rebuild.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <paramref name="value"/> in the implicit tuple conversion Roslyn
    /// reports for <paramref name="expression"/>, when an element crosses a
    /// 64-bit integer; <paramref name="value"/> itself otherwise. Tuple
    /// literal syntax is left alone, because Roslyn converts its elements
    /// where they are written; a cast literal is not, because its conversion
    /// is reported on the cast.
    /// </summary>
    private IrExpression WithImplicitTupleConversion(
        ExpressionSyntax expression,
        IrExpression value
    )
    {
        if (expression is TupleExpressionSyntax || CarriesNoImplicitConversion(expression, value))
            return value;

        var conversion = _semantic.GetConversion(expression);

        if (!conversion.IsImplicit || conversion.IsIdentity)
            return value;

        var typeInfo = _semantic.GetTypeInfo(expression);

        return ConvertedTuple(
                expression,
                value,
                typeInfo.Type,
                typeInfo.ConvertedType,
                TupleElementConversions.Implicit
            ) ?? value;
    }

    /// <summary>
    /// <paramref name="value"/> in the explicit tuple conversion C# applies
    /// at <paramref name="site"/> — a cast (<c>((int, int))longs</c>), or the
    /// loop variable type of a <c>foreach</c> that converts each element —
    /// with each element converted as the scalar cast would in the site's
    /// checked context; <c>null</c> when no element converts.
    /// </summary>
    internal IrExpression? ExplicitTupleConversion(
        SyntaxNode site,
        IrExpression value,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    ) => ConvertedTuple(site, value, sourceType, targetType, ExplicitElementConversionsAt(site));

    /// <summary>
    /// An explicit cast of a tuple literal (<c>((long, long))(a, b)</c>) with
    /// each element converted where it is written, so the elements evaluate
    /// and convert in source order as C# does — a missing nullable element
    /// throws before the next one runs — and each converts once;
    /// <c>null</c> when the operand is not a literal or no element converts.
    /// The literal needs no type of its own: an element without one
    /// (<c>default</c>, <c>null</c>) takes the target's.
    /// </summary>
    private IrExpression? ExplicitTupleLiteralConversion(
        CastExpressionSyntax cast,
        ITypeSymbol targetType
    )
    {
        if (
            KeepsTuplesUnconverted
            || TupleLiteralOf(cast.Expression) is not { } literal
            || TupleTargetOf(literal, targetType) is not { } target
        )
            return null;

        var elements = ExplicitElementConversionsAt(cast);

        return ConvertsLiteral(literal, target, elements)
            ? ConvertedTupleLiteral(cast, literal, target, elements)
            : null;
    }

    /// <summary>
    /// The tuple type <paramref name="literal"/> is built as when it converts
    /// to <paramref name="to"/> (nullable unwrapped); <c>null</c> when
    /// <paramref name="to"/> is not a tuple of the literal's arity.
    /// </summary>
    private static INamedTypeSymbol? TupleTargetOf(TupleExpressionSyntax literal, ITypeSymbol to) =>
        to.UnwrapNullableValueType() is INamedTypeSymbol { IsTupleType: true } target
        && target.TupleElements.Length == literal.Arguments.Count
            ? target
            : null;

    /// <summary>
    /// Whether an element of <paramref name="literal"/>, at any depth,
    /// converts on its way to its element of <paramref name="target"/>.
    /// </summary>
    private bool ConvertsLiteral(
        TupleExpressionSyntax literal,
        INamedTypeSymbol target,
        TupleElementConversions elements
    ) =>
        literal
            .Arguments.Zip(target.TupleElements)
            .Any(pair => ConvertsLiteralElement(pair.First.Expression, pair.Second.Type, elements));

    private bool ConvertsLiteralElement(
        ExpressionSyntax element,
        ITypeSymbol to,
        TupleElementConversions elements
    )
    {
        if (_semantic.GetTypeInfo(element).Type is { } from)
            return elements.ConvertsElement(from, to);

        return TupleLiteralOf(element) is { } nested
            && TupleTargetOf(nested, to) is { } nestedTarget
            && ConvertsLiteral(nested, nestedTarget, elements);
    }

    /// <summary>
    /// <paramref name="literal"/> as the tuple type <paramref name="target"/>,
    /// element by element; <c>null</c> when it has more elements than a flat
    /// tuple holds, which the literal's own extraction reports.
    /// </summary>
    private IrTupleLiteral? ConvertedTupleLiteral(
        SyntaxNode site,
        TupleExpressionSyntax literal,
        INamedTypeSymbol target,
        TupleElementConversions elements
    )
    {
        if (literal.Arguments.Count > SymbolHelper.MaxFlatTupleArity)
            return null;

        var converted = literal
            .Arguments.Select(
                (argument, index) =>
                    ConvertedLiteralElement(
                        site,
                        argument.Expression,
                        target.TupleElements[index].Type,
                        elements
                    )
            )
            .ToList();

        return TupleLiteralAs(target, converted);
    }

    /// <summary>
    /// One element of an explicitly cast tuple literal, converted to
    /// <paramref name="to"/>: a nested literal converts its own elements, an
    /// element Roslyn converts implicitly (or one with no type of its own)
    /// is extracted with that conversion, and any other takes the explicit
    /// conversion of its types — as its underlying type when nullable
    /// analysis proves a nullable element present, so no unwrap is emitted
    /// for it.
    /// </summary>
    private IrExpression ConvertedLiteralElement(
        SyntaxNode site,
        ExpressionSyntax element,
        ITypeSymbol to,
        TupleElementConversions elements
    )
    {
        if (NestedLiteralConversion(site, element, to, elements) is { } nested)
            return nested;

        var value = Extract(element);
        var from = _semantic.GetTypeInfo(element).Type;

        if (from is null || _semantic.GetConversion(element).IsImplicit)
            return value;

        var source = IsKnownPresent(element) ? from.UnwrapNullableValueType() : from;

        return ConvertedElement(site, value, source, to, elements);
    }

    /// <summary>
    /// <paramref name="element"/> converted as a tuple literal nested in a
    /// cast one; <c>null</c> when it is not a literal, <paramref name="to"/>
    /// is not a tuple of its arity, or it has more elements than a flat
    /// tuple holds.
    /// </summary>
    private IrTupleLiteral? NestedLiteralConversion(
        SyntaxNode site,
        ExpressionSyntax element,
        ITypeSymbol to,
        TupleElementConversions elements
    )
    {
        if (
            TupleLiteralOf(element) is not { } literal
            || TupleTargetOf(literal, to) is not { } target
        )
            return null;

        return ConvertedTupleLiteral(site, literal, target, elements);
    }

    /// <summary>
    /// Whether nullable analysis states the value present (the read follows
    /// a check, or converts a non-null value: <c>(int?)2</c>). A
    /// nullable-disabled context states nothing, and there the element keeps
    /// its unwrap.
    /// </summary>
    private bool IsKnownPresent(ExpressionSyntax expression) =>
        _semantic.GetTypeInfo(expression).Nullability.FlowState == NullableFlowState.NotNull;

    /// <summary>A tuple literal of the tuple type <paramref name="type"/>.</summary>
    private IrTupleLiteral TupleLiteralAs(
        INamedTypeSymbol type,
        IReadOnlyList<IrExpression> elements
    ) => new(elements, IrTypeRefMapper.Map(type, _originResolver, _target) as IrTupleTypeRef);

    /// <summary>
    /// Whether the target keeps a tuple as it is across an integer width:
    /// Dart's <c>int</c> holds both, so its records need no conversion.
    /// </summary>
    private bool KeepsTuplesUnconverted => _target is Metano.Annotations.TargetLanguage.Dart;

    /// <summary>The tuple literal <paramref name="expression"/> is, through any parentheses; <c>null</c> otherwise.</summary>
    private static TupleExpressionSyntax? TupleLiteralOf(ExpressionSyntax expression) =>
        expression switch
        {
            TupleExpressionSyntax literal => literal,
            ParenthesizedExpressionSyntax parenthesized => TupleLiteralOf(parenthesized.Expression),
            _ => null,
        };

    /// <summary>The explicit-cast element rule in the checked context of <paramref name="site"/>.</summary>
    private TupleElementConversions ExplicitElementConversionsAt(SyntaxNode site) =>
        TupleElementConversions.Explicit(CheckedContexts.IsChecked(site, _semantic.Compilation));

    /// <summary>
    /// <paramref name="value"/>, of the tuple type <paramref name="from"/>,
    /// rebuilt as <paramref name="to"/>; <c>null</c> when the types are not
    /// two tuples of one arity or no element converts. A nullable source
    /// converts lifted into a nullable target: <c>null</c> stays
    /// <c>null</c>. Into a non-nullable target — an explicit conversion only
    /// — it unwraps first, with the .NET throw when the value is missing.
    /// </summary>
    private IrExpression? ConvertedTuple(
        SyntaxNode site,
        IrExpression value,
        ITypeSymbol? from,
        ITypeSymbol? to,
        TupleElementConversions elements
    )
    {
        if (
            KeepsTuplesUnconverted
            || from is null
            || to is null
            || elements.ConvertingTuples(from, to) is not var (source, target)
        )
            return null;

        if (source.TupleElements.Length > SymbolHelper.MaxFlatTupleArity)
        {
            return ReportUnsupported(
                site,
                $"tuple conversion over more than {SymbolHelper.MaxFlatTupleArity} elements"
            );
        }

        var (operand, isLifted) = UnwrappedOrLifted(value, from, to);

        return BindOnce(
            operand,
            IrEvaluationTraits.IsSideEffectFree(operand),
            tuple => RebuiltTuple(site, tuple, isLifted, source, target, elements)
        );
    }

    /// <summary>
    /// How a value of type <paramref name="from"/> enters its conversion to
    /// <paramref name="to"/>: a nullable one unwraps, with the .NET throw
    /// when it is missing, into a non-nullable target and converts lifted
    /// into a nullable one; any other is the value itself.
    /// </summary>
    private (IrExpression Value, bool IsLifted) UnwrappedOrLifted(
        IrExpression value,
        ITypeSymbol from,
        ITypeSymbol to
    )
    {
        if (UnwrapsNullableValue(from, to))
            return (GuardedValueOrThrow(value), IsLifted: false);

        return (value, IsLifted: from.IsNullableValueType());
    }

    private IrExpression RebuiltTuple(
        SyntaxNode site,
        IrExpression tuple,
        bool isLifted,
        INamedTypeSymbol source,
        INamedTypeSymbol target,
        TupleElementConversions elements
    )
    {
        var operand = new ValueOperand(source, tuple);

        var converted = Enumerable
            .Range(0, source.TupleElements.Length)
            .Select(index =>
                ConvertedElement(
                    site,
                    AsValue(operand.ElementAt(index)).Value,
                    source.TupleElements[index].Type,
                    target.TupleElements[index].Type,
                    elements
                )
            )
            .ToList();

        var rebuilt = TupleLiteralAs(target, converted);

        if (!isLifted)
            return rebuilt;

        var isMissing = new IrBinaryExpression(tuple, IrBinaryOp.Equal, NullLiteral);

        return new IrConditionalExpression(isMissing, NullLiteral, rebuilt);
    }

    /// <summary>
    /// One element read, converted as its own types need: a nested tuple is
    /// rebuilt, a numeric element takes its scalar conversion, any other
    /// element is the read itself. A nullable numeric element converts
    /// lifted into a nullable one and unwraps, with the .NET throw, into a
    /// non-nullable one.
    /// </summary>
    private IrExpression ConvertedElement(
        SyntaxNode site,
        IrExpression element,
        ITypeSymbol from,
        ITypeSymbol to,
        TupleElementConversions elements
    )
    {
        if (ConvertedTuple(site, element, from, to, elements) is { } nested)
            return nested;

        if (elements.ScalarEndpoints(from, to) is not var (source, target))
            return element;

        var (operand, isLifted) = UnwrappedOrLifted(element, from, to);

        return new IrNumericConversion(operand, source, target, elements.IsChecked, isLifted);
    }

    /// <summary>
    /// Which element conversions a tuple conversion makes explicit:
    /// <paramref name="Converts"/> decides for a pair of numeric endpoints
    /// (the implicit rule or the explicit-cast rule), and
    /// <paramref name="IsChecked"/> is the context of an explicit cast.
    /// </summary>
    private sealed record TupleElementConversions(
        Func<IrNumericEndpoint, IrNumericEndpoint, bool> Converts,
        bool IsChecked
    )
    {
        /// <summary>The implicit rule: an element converts when it crosses a 64-bit integer.</summary>
        public static TupleElementConversions Implicit { get; } =
            new(NumericEndpoints.CrossesSixtyFourBits, IsChecked: false);

        /// <summary>The explicit-cast rule, in the cast's checked context.</summary>
        public static TupleElementConversions Explicit(bool isChecked) =>
            new(NumericEndpoints.IsLoweredExplicitly, isChecked);

        /// <summary>
        /// The two tuple types (nullable unwrapped) when
        /// <paramref name="from"/> and <paramref name="to"/> are tuples of
        /// one arity with an element, at any depth, that converts;
        /// <c>null</c> otherwise.
        /// </summary>
        public (INamedTypeSymbol Source, INamedTypeSymbol Target)? ConvertingTuples(
            ITypeSymbol? from,
            ITypeSymbol? to
        )
        {
            if (
                from?.UnwrapNullableValueType() is not INamedTypeSymbol { IsTupleType: true } source
                || to?.UnwrapNullableValueType()
                    is not INamedTypeSymbol { IsTupleType: true } target
                || source.TupleElements.Length != target.TupleElements.Length
            )
                return null;

            var converts = source
                .TupleElements.Zip(target.TupleElements)
                .Any(pair => ConvertsElement(pair.First.Type, pair.Second.Type));

            return converts ? (source, target) : null;
        }

        /// <summary>Whether an element of type <paramref name="from"/> converts on its way to <paramref name="to"/>: a tuple with a converting element, or a numeric element.</summary>
        public bool ConvertsElement(ITypeSymbol from, ITypeSymbol to) =>
            ConvertingTuples(from, to) is not null || ScalarEndpoints(from, to) is not null;

        /// <summary>The endpoints of a numeric element that converts; <c>null</c> for any other element.</summary>
        public (IrNumericEndpoint From, IrNumericEndpoint To)? ScalarEndpoints(
            ITypeSymbol from,
            ITypeSymbol to
        )
        {
            if (
                NumericEndpoints.Of(from) is not { } source
                || NumericEndpoints.Of(to) is not { } target
            )
                return null;

            return Converts(source, target) ? (source, target) : null;
        }
    }
}
