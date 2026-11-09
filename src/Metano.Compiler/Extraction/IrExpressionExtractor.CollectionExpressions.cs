using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Collection expressions (<c>[a, ..b, c]</c>, C# 12). A list or array target
/// is an array literal; a set or dictionary target is a construction from the
/// same array literal, so every element and every spread is evaluated once, in
/// source order, and handed to the collection as a single iterable — the set
/// adds each value, the map each <c>[key, value]</c> entry (a
/// <c>KeyValuePair</c> lowers to that tuple, and enumerating a map or a list of
/// pairs yields it). An empty set or dictionary is an empty construction.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrExpression ExtractCollectionExpression(CollectionExpressionSyntax coll)
    {
        // The operation's type is the collection the expression builds, a cast
        // target included (`(HashSet<int>)[1, 2]`), which the expression's
        // converted type does not report.
        var operation = _semantic.GetOperation(coll) as ICollectionExpressionOperation;

        if (operation?.Type is not INamedTypeSymbol target)
            return new IrArrayLiteral(ExtractCollectionElements(coll));

        if (IsSetTarget(target))
            return NewCollectionOf(new IrSetTypeRef(ToIrTypeArgument(target, 0)), coll);

        if (!IsMapTarget(target))
            return new IrArrayLiteral(ExtractCollectionElements(coll));

        if (coll.Elements.Count > 0 && AddsThroughUserDefinedMethod(operation))
            return ReportUnsupported(
                coll,
                coll.Kind().ToString(),
                $"Collection expression '{coll}' builds '{target.ToDisplayString()}' through a "
                    + "user-defined Add(KeyValuePair) method, whose semantics the transpiler "
                    + "does not reproduce. Build the dictionary with explicit Add calls instead."
            );

        var mapType = new IrMapTypeRef(ToIrTypeArgument(target, 0), ToIrTypeArgument(target, 1));

        return NewCollectionOf(mapType, coll);
    }

    /// <summary>
    /// <c>new T()</c> for an empty collection expression; otherwise
    /// <c>new T([elements])</c>, the construction from one iterable.
    /// </summary>
    private IrNewExpression NewCollectionOf(IrTypeRef type, CollectionExpressionSyntax coll)
    {
        if (coll.Elements.Count == 0)
            return new IrNewExpression(type, []);

        var elements = new IrArrayLiteral(ExtractCollectionElements(coll));

        return new IrNewExpression(type, [new IrArgument(elements)]);
    }

    /// <summary>
    /// The elements in source order: a plain element extracts directly, a
    /// spread (<c>..source</c>) becomes an <see cref="IrSpreadExpression"/> so
    /// a backend re-emits <c>...source</c> instead of dropping it. Any other
    /// element kind (a later C# version's) is reported.
    /// </summary>
    private List<IrExpression> ExtractCollectionElements(CollectionExpressionSyntax coll) =>
        coll
            .Elements.Select<CollectionElementSyntax, IrExpression>(element =>
                element switch
                {
                    ExpressionElementSyntax expression => Extract(expression.Expression),
                    SpreadElementSyntax spread => ExtractSpread(spread),
                    _ => ReportUnsupported(element, element.Kind().ToString()),
                }
            )
            .ToList();

    /// <summary>
    /// <c>..source</c> with the conversion C# applies to each element it
    /// spreads. A converted string or dictionary spread is reported: its
    /// elements are not what the target iterates.
    /// </summary>
    private IrExpression ExtractSpread(SpreadElementSyntax spread)
    {
        var source = SpreadSourceOf(spread.Expression);
        var conversion = SpreadElementConversionOf(spread);

        if (conversion is not null && source is not IrSpreadSource.Iterable)
        {
            return ReportUnsupported(
                spread,
                "spread of a string or dictionary whose elements convert to a 64-bit integer"
            );
        }

        return new IrSpreadExpression(Extract(spread.Expression), source, conversion);
    }

    /// <summary>
    /// The conversion from the spread's element type to the element type of
    /// the collection being built, when it crosses a 64-bit integer (a
    /// scalar, or a tuple element by element); <c>null</c> otherwise.
    /// </summary>
    private IrSpreadElementConversion? SpreadElementConversionOf(SpreadElementSyntax spread)
    {
        if (
            _semantic.GetOperation(spread)
                is not ISpreadOperation
                {
                    ElementType: { } from,
                    Parent: ICollectionExpressionOperation { Type: { } collection },
                }
            || CollectionElementTypeOf(collection) is not { } to
        )
            return null;

        var name = NextSpreadElementName();
        var element = new IrIdentifier(name);
        var implicitly = new TupleElementConversions(NumericEndpoints.CrossesSixtyFourBits, false);
        var converted = ConvertedElement(spread, element, from, to, implicitly);

        return ReferenceEquals(converted, element)
            ? null
            : new IrSpreadElementConversion(name, converted);
    }

    /// <summary>
    /// The element type of a collection-expression target: an array's
    /// element, the <c>T</c> of the <c>IEnumerable&lt;T&gt;</c> the type is or
    /// implements, else its single type argument (<c>Span&lt;T&gt;</c>).
    /// </summary>
    private static ITypeSymbol? CollectionElementTypeOf(ITypeSymbol collection)
    {
        if (collection is IArrayTypeSymbol array)
            return array.ElementType;

        if (collection is not INamedTypeSymbol named)
            return null;

        var enumerable = named
            .AllInterfaces.Prepend(named)
            .FirstOrDefault(type =>
                type.OriginalDefinition.SpecialType
                is SpecialType.System_Collections_Generic_IEnumerable_T
            );

        if (enumerable is not null)
            return enumerable.TypeArguments[0];

        return named.TypeArguments is [var single] ? single : null;
    }

    private IrSpreadSource SpreadSourceOf(ExpressionSyntax spreadOperand) =>
        _semantic.GetTypeInfo(spreadOperand).Type switch
        {
            { SpecialType: SpecialType.System_String } => IrSpreadSource.String,
            INamedTypeSymbol named when IsMapTarget(named) => IrSpreadSource.Map,
            _ => IrSpreadSource.Iterable,
        };

    private IrTypeRef ToIrTypeArgument(INamedTypeSymbol type, int index) =>
        IrTypeRefMapper.Map(type.TypeArguments[index], _originResolver, _target);

    /// <summary>
    /// A BCL dictionary has no public single-argument <c>Add</c>, so a
    /// non-empty collection expression over <c>Dictionary</c>,
    /// <c>SortedDictionary</c> or <c>SortedList</c> compiles only through a
    /// user-defined <c>Add(KeyValuePair)</c> (an extension method) that the
    /// construction calls per element — its duplicate-key behaviour is the
    /// user's, not a map's. A collection-builder type (<c>ImmutableDictionary</c>)
    /// hands the elements to the BCL factory instead, which keeps the last
    /// value of a repeated key, as a JS <c>Map</c> does.
    /// </summary>
    private static bool AddsThroughUserDefinedMethod(ICollectionExpressionOperation operation) =>
        operation.ConstructMethod is { MethodKind: MethodKind.Constructor };

    /// <summary>The set types a collection expression lowers to the runtime <c>HashSet</c>.</summary>
    private static bool IsSetTarget(INamedTypeSymbol target) =>
        target.TypeArguments.Length == 1 && target.IsSetLike();

    /// <summary>
    /// The dictionary types a collection expression lowers to a JS
    /// <c>Map</c>: the dictionary-like types the type mapper maps to
    /// <c>Map</c>, plus <c>SortedDictionary</c> and <c>SortedList</c>.
    /// </summary>
    private static bool IsMapTarget(INamedTypeSymbol target) =>
        target.TypeArguments.Length == 2
        && (
            target.IsDictionaryLike() || target.OriginalDefinition.IsBclSortedDictionaryDefinition()
        );
}
