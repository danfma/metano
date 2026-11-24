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
            return ListLiteralOf(coll, operation);

        if (
            IsSetTarget(target)
            || StageCollectionOf(target) is { Kind: IrCollectionKind.ImmutableHashSet }
        )
            return NewCollectionOf(
                (IrTypeRef?)StageCollectionOf(target)
                    ?? new IrSetTypeRef(ToIrTypeArgument(target, 0)),
                coll
            );

        if (
            !IsMapTarget(target)
            && StageCollectionOf(target) is not { Kind: IrCollectionKind.ImmutableDictionary }
        )
            return IsListShapedTarget(target)
                ? ListLiteralOf(coll, operation)
                : UnmodeledTargetOf(coll, operation, target);

        if (coll.Elements.Count > 0 && AddsThroughUserDefinedMethod(operation))
            return ReportUnsupported(
                coll,
                coll.Kind().ToString(),
                $"Collection expression '{coll}' builds '{target.ToDisplayString()}' through a "
                    + "user-defined Add(KeyValuePair) method, whose semantics the transpiler "
                    + "does not reproduce. Build the dictionary with explicit Add calls instead."
            );

        var mapType = (IrTypeRef?)StageCollectionOf(target)
            ?? new IrMapTypeRef(ToIrTypeArgument(target, 0), ToIrTypeArgument(target, 1));

        return NewCollectionOf(mapType, coll);
    }

    /// <summary>
    /// Whether a list-shaped literal is what C# builds for
    /// <paramref name="target"/>: a type the type mapper maps to a native
    /// array or an iterable (<c>IEnumerable&lt;T&gt;</c>, <c>JsArray&lt;T&gt;</c>),
    /// a span, or a collection whose construction from a
    /// literal is modelled (<see cref="TargetCollectionOf"/>). Any other type
    /// is built through a collection builder or an <c>Add</c> the lowering does
    /// not reproduce (<c>ImmutableSortedSet</c>, <c>FrozenSet</c>,
    /// <c>ImmutableQueue</c>, a user collection type; see
    /// <see cref="UnmodeledTargetOf"/>, #581).
    /// </summary>
    private bool IsListShapedTarget(INamedTypeSymbol target) =>
        IsSpan(target)
        || IrTypeRefMapper.Map(target, _originResolver, _target)
            is IrArrayTypeRef
                or IrIterableTypeRef
        || TargetCollectionOf(target) is not null;

    private static bool IsSpan(INamedTypeSymbol type) =>
        type.ContainingType is null
        && type.ContainingNamespace?.ToDisplayString() == "System"
        && type.MetadataName is "Span`1" or "ReadOnlySpan`1";

    /// <summary>
    /// A collection expression over a type whose construction is not modelled
    /// (#581) reports <c>MS0001</c>. Where the array gives what .NET observes —
    /// an order-preserving list-like such as <c>Collection&lt;T&gt;</c>,
    /// <c>ObservableCollection&lt;T&gt;</c> or <c>ImmutableQueue&lt;T&gt;</c>, or
    /// a literal too short to reorder — the array literal stays; where it
    /// would be a silent wrong value, the expression throws instead
    /// (<see cref="RebuildsElements"/>).
    /// </summary>
    private IrExpression UnmodeledTargetOf(
        CollectionExpressionSyntax coll,
        ICollectionExpressionOperation? operation,
        INamedTypeSymbol target
    )
    {
        var typeName = target.ToDisplayString();

        if (RebuildsElements(coll, target))
            return ReportUnsupported(
                coll,
                coll.Kind().ToString(),
                $"Collection expression '{coll}' builds '{typeName}', which orders, "
                    + "deduplicates or adds its elements in a way the transpiler does not "
                    + "model, so the expression throws at run time. Build the collection "
                    + "explicitly instead."
            );

        ReportUnsupportedFeature(
            coll,
            $"Collection expression '{coll}' builds '{typeName}', which the transpiler does "
                + "not model: a plain array stands in for it, so enumerating and indexing it "
                + "match .NET, but the members of the type do not exist on the value."
        );

        return ListLiteralOf(coll, operation);
    }

    /// <summary>
    /// Whether the array would enumerate other elements than <paramref name="target"/>
    /// holds. The decision follows the collection whose construction runs: the
    /// type that declares the <c>Add</c> the elements bind to (a user subclass
    /// of <c>List&lt;T&gt;</c> or <c>Collection&lt;T&gt;</c> inherits theirs), or
    /// the target itself for a collection-builder type. A user-declared
    /// <c>Add</c> may do anything, so any element rebuilds; a BCL type that
    /// sorts, deduplicates or reverses (<see cref="IsReorderingBclCollection"/>)
    /// rebuilds two or more elements (or a spread).
    /// </summary>
    private static bool RebuildsElements(CollectionExpressionSyntax coll, INamedTypeSymbol target)
    {
        if (coll.Elements.Count == 0)
            return false;

        var builder = AddDeclaringTypeOf(target) ?? target;

        if (!builder.ContainingAssembly.IsBclAssembly())
            return true;

        var canReorder =
            coll.Elements.Count > 1 || coll.Elements.Any(e => e is SpreadElementSyntax);

        return canReorder && IsReorderingBclCollection(builder);
    }

    /// <summary>
    /// The nearest type in <paramref name="target"/>'s base chain that declares a
    /// public one-parameter instance <c>Add</c>: the one a collection expression
    /// binds to, since a derived declaration hides or overrides the base's;
    /// <c>null</c> when none does (a collection-builder type).
    /// </summary>
    private static INamedTypeSymbol? AddDeclaringTypeOf(INamedTypeSymbol target)
    {
        for (var type = target; type is not null; type = type.BaseType)
        {
            var declaresAdd = type.GetMembers("Add")
                .OfType<IMethodSymbol>()
                .Any(method =>
                    method
                        is {
                            IsStatic: false,
                            Parameters.Length: 1,
                            DeclaredAccessibility: Accessibility.Public,
                        }
                );

            if (declaresAdd)
                return type;
        }

        return null;
    }

    private static bool IsReorderingBclCollection(INamedTypeSymbol target) =>
        target.OriginalDefinition.ToDisplayString()
            is "System.Collections.Immutable.ImmutableSortedSet<T>"
                or "System.Collections.Immutable.ImmutableStack<T>"
                or "System.Collections.Frozen.FrozenSet<T>"
                or "System.Collections.Concurrent.ConcurrentBag<T>"
                or "System.Collections.Generic.HashSet<T>"
                or "System.Collections.Generic.SortedSet<T>";

    /// <summary>
    /// The array literal of a list or array target, with the element type the
    /// target declares where the elements alone do not say it, and the
    /// collection C# builds for a list-shaped target (spec 011 data-model § 2,
    /// B1).
    /// </summary>
    private IrArrayLiteral ListLiteralOf(
        CollectionExpressionSyntax coll,
        ICollectionExpressionOperation? operation
    ) =>
        new(
            ExtractCollectionElements(coll),
            operation is null ? null : DeclaredElementType(coll, operation.Type, operation.Elements)
        )
        {
            TargetCollection = TargetCollectionOf(operation?.Type),
        };

    /// <summary>
    /// The collection a list-shaped collection expression constructs
    /// (<see cref="ConstructedCollectionOf"/>); <c>null</c> for an array, a
    /// span or any other target.
    /// </summary>
    private IrCollectionTypeRef? TargetCollectionOf(ITypeSymbol? target) =>
        StageCollectionOf(target) is { } collection ? ConstructedCollectionOf(collection) : null;

    /// <summary>
    /// The stage 011c collection ref of a collection-expression target, the
    /// one rule every collection expression detects its collection by;
    /// <c>null</c> for a type the 011c kinds do not cover. An
    /// <c>IImmutable*</c> interface target is the concrete immutable collection
    /// C# builds for it (its <c>[CollectionBuilder]</c>: <c>ImmutableList</c>,
    /// <c>ImmutableHashSet</c>, <c>ImmutableDictionary</c>), so the value has the
    /// runtime class the interface's members exist on.
    /// </summary>
    private IrCollectionTypeRef? StageCollectionOf(ITypeSymbol? target) =>
        target is INamedTypeSymbol named
        && IrTypeRefMapper.Map(named, _originResolver, _target) is IrCollectionTypeRef collection
            ? collection with
            {
                Kind = BuiltImmutableKind(collection.Kind),
            }
            : null;

    /// <summary>The immutable class a collection expression builds for an <c>IImmutable*</c> target; any other kind is its own.</summary>
    private static IrCollectionKind BuiltImmutableKind(IrCollectionKind kind) =>
        kind switch
        {
            IrCollectionKind.IImmutableList => IrCollectionKind.ImmutableList,
            IrCollectionKind.IImmutableSet => IrCollectionKind.ImmutableHashSet,
            IrCollectionKind.IImmutableDictionary => IrCollectionKind.ImmutableDictionary,
            _ => kind,
        };

    /// <summary>
    /// What C# builds for a list-shaped target: a <c>List&lt;T&gt;</c> for
    /// <c>List&lt;T&gt;</c>, <c>IList&lt;T&gt;</c> and <c>ICollection&lt;T&gt;</c>,
    /// a read-only array for <c>IReadOnlyList&lt;T&gt;</c> /
    /// <c>IReadOnlyCollection&lt;T&gt;</c> (kept as that interface's kind), the
    /// immutable list itself for <c>ImmutableList&lt;T&gt;</c> /
    /// <c>ImmutableArray&lt;T&gt;</c> (their collection builder);
    /// <c>null</c> for any other kind.
    /// </summary>
    private static IrCollectionTypeRef? ConstructedCollectionOf(IrCollectionTypeRef collection) =>
        collection.Kind switch
        {
            IrCollectionKind.List or IrCollectionKind.IList or IrCollectionKind.ICollection =>
                collection with
                {
                    Kind = IrCollectionKind.List,
                },
            IrCollectionKind.IReadOnlyList
            or IrCollectionKind.IReadOnlyCollection
            or IrCollectionKind.ImmutableList
            or IrCollectionKind.ImmutableArray => collection,
            _ => null,
        };

    /// <summary>
    /// <c>new T()</c> for an empty collection expression; otherwise
    /// <c>new T([elements])</c>, the construction from one iterable.
    /// </summary>
    private IrNewExpression NewCollectionOf(IrTypeRef type, CollectionExpressionSyntax coll)
    {
        if (coll.Elements.Count == 0)
            return new IrNewExpression(type, []);

        var operation = _semantic.GetOperation(coll) as ICollectionExpressionOperation;

        var elements = new IrArrayLiteral(
            ExtractCollectionElements(coll),
            operation is null ? null : DeclaredElementType(coll, operation.Type, operation.Elements)
        );

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
    /// spreads; a string spreads its UTF-16 code units (a <c>char</c> read as
    /// a number, FR-071). (A dictionary spreads key-value pairs, which C#
    /// never converts: a <c>KeyValuePair&lt;int, int&gt;</c> spread into a
    /// collection of <c>KeyValuePair&lt;long, long&gt;</c> is a compile error.)
    /// </summary>
    private IrExpression ExtractSpread(SpreadElementSyntax spread)
    {
        var source = SpreadSourceOf(spread.Expression);
        var converting = ConvertingSpreadElementTypesOf(spread);

        var conversion = converting is var (from, to)
            ? SpreadElementConversion(spread, NextSpreadElementName(), from, to)
            : null;

        return new IrSpreadExpression(Extract(spread.Expression), source, conversion);
    }

    /// <summary>
    /// The spread's element type and the element type of the collection
    /// being built, when the conversion between them crosses a 64-bit integer
    /// (a scalar, or a tuple element by element); <c>null</c> otherwise, and
    /// always for Dart, which spreads the source as it is.
    /// </summary>
    private (ITypeSymbol From, ITypeSymbol To)? ConvertingSpreadElementTypesOf(
        SpreadElementSyntax spread
    )
    {
        if (KeepsTuplesUnconverted)
            return null;

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

        return TupleElementConversions.Implicit(_context.NumericLowering).ConvertsElement(from, to)
            ? (from, to)
            : null;
    }

    /// <summary>
    /// The conversion of one spread element, bound to
    /// <paramref name="name"/>, from <paramref name="from"/> to
    /// <paramref name="to"/>.
    /// </summary>
    private IrSpreadElementConversion SpreadElementConversion(
        SpreadElementSyntax spread,
        string name,
        ITypeSymbol from,
        ITypeSymbol to
    )
    {
        var converted = ConvertedElement(
            spread,
            new IrIdentifier(name),
            from,
            to,
            TupleElementConversions.Implicit(_context.NumericLowering)
        );

        return new IrSpreadElementConversion(name, converted);
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
            { } type when IsMapSpreadOperand(type) => IrSpreadSource.Map,
            _ => IrSpreadSource.Iterable,
        };

    private static bool IsMapSpreadOperand(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol typeParameter)
            return typeParameter.ConstraintTypes.Any(IsMapSpreadOperand);

        if (type is not INamedTypeSymbol named)
            return false;

        for (var current = named; current is not null; current = current.BaseType)
        {
            if (IsMapTarget(current))
                return true;
        }

        return named.AllInterfaces.Any(IsMapTarget);
    }

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
