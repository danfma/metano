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
/// <see cref="IrNumericConversion"/> its own conversion needs. The source is
/// evaluated once. A tuple literal is not rebuilt: Roslyn converts its
/// elements where they are written. Dart keeps the tuple as it is: its
/// <c>int</c> holds both widths, so a record of them needs no rebuild.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// <paramref name="value"/> in the implicit tuple conversion Roslyn
    /// reports for <paramref name="expression"/>, when an element crosses a
    /// 64-bit integer; <paramref name="value"/> itself otherwise.
    /// </summary>
    private IrExpression WithImplicitTupleConversion(
        ExpressionSyntax expression,
        IrExpression value
    )
    {
        if (value is IrTupleLiteral || CarriesNoImplicitConversion(expression, value))
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
    /// An explicit tuple cast (<c>((int, int))longs</c>) whose elements convert
    /// numerically, each as the scalar cast would in the cast's checked
    /// context; <c>null</c> when no element does.
    /// </summary>
    private IrExpression? ExplicitTupleConversion(
        CastExpressionSyntax cast,
        IrExpression operand,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    ) => ExplicitTupleConversion((SyntaxNode)cast, operand, sourceType, targetType);

    /// <summary>
    /// <paramref name="value"/> in the explicit tuple conversion C# applies
    /// at <paramref name="site"/> — a cast, or the element conversion of a
    /// <c>foreach</c> — in the site's checked context; <c>null</c> when no
    /// element converts.
    /// </summary>
    internal IrExpression? ExplicitTupleConversion(
        SyntaxNode site,
        IrExpression value,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    )
    {
        var isChecked = CheckedContexts.IsChecked(site, _semantic.Compilation);

        return ConvertedTuple(
            site,
            value,
            sourceType,
            targetType,
            TupleElementConversions.Explicit(isChecked)
        );
    }

    /// <summary>
    /// <paramref name="value"/>, of the tuple type <paramref name="from"/>,
    /// rebuilt as <paramref name="to"/>; <c>null</c> when the types are not
    /// two tuples of one arity or no element converts. A nullable source
    /// converts lifted: <c>null</c> stays <c>null</c>.
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
            _target is Metano.Annotations.TargetLanguage.Dart
            || elements.ConvertingTuples(from, to) is not var (source, target)
        )
            return null;

        var isLifted = from.IsNullableValueType();

        if (source.TupleElements.Length > SymbolHelper.MaxFlatTupleArity)
        {
            return ReportUnsupported(
                site,
                $"tuple conversion over more than {SymbolHelper.MaxFlatTupleArity} elements"
            );
        }

        return BindOnce(
            value,
            IrEvaluationTraits.IsSideEffectFree(value),
            tuple => RebuiltTuple(site, tuple, isLifted, source, target, elements)
        );
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

        var rebuilt = new IrTupleLiteral(
            converted,
            IrTypeRefMapper.Map(target, _originResolver, _target) as IrTupleTypeRef
        );

        if (!isLifted)
            return rebuilt;

        var isMissing = new IrBinaryExpression(tuple, IrBinaryOp.Equal, NullLiteral);

        return new IrConditionalExpression(isMissing, NullLiteral, rebuilt);
    }

    /// <summary>
    /// One element read, converted as its own types need: a nested tuple is
    /// rebuilt, a numeric element takes its scalar conversion, any other
    /// element is the read itself.
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

        return new IrNumericConversion(
            element,
            source,
            target,
            elements.IsChecked,
            IsLifted: from.IsNullableValueType()
        );
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
