using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The collection initializers of the stage 011c <c>List</c>, <c>HashSet</c>
/// and <c>Dictionary</c> (spec 011 FR-007, contracts/runtime-collections.md §
/// Compiler contract): <c>new List&lt;T&gt; { a, b }</c>,
/// <c>new Dictionary&lt;K, V&gt; { { k, v } }</c> and <c>{ [k] = v }</c>, after
/// any of their constructors, become one <see cref="IrCollectionInitialization"/>
/// that the target lowers to a factory, with no let-bound instance. A target
/// that keeps native collections, a derived class, and an initializer whose
/// elements are not all plain <c>Add</c> calls (or all plain index writes) keep
/// the general initializer lowering.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private IrCollectionInitialization? TryExtractCollectionInitialization(
        IrExpression creation,
        ITypeSymbol? createdType,
        InitializerExpressionSyntax initializer
    )
    {
        if (
            FactoryCollectionOf(createdType, initializer)
            is not
            (var named, var type, var operation)
        )
            return null;

        var form = initializer.IsKind(SyntaxKind.CollectionInitializerExpression)
            ? IrCollectionInitializerForm.Add
            : IrCollectionInitializerForm.Assign;

        var elements =
            form is IrCollectionInitializerForm.Add
                ? AddElementsOf(initializer, named)
                : AssignElementsOf(initializer, type);

        if (elements is null)
            return null;

        var construction = ConstructionOf(operation.Constructor);

        return new IrCollectionInitialization(
            creation,
            type,
            form,
            construction,
            construction is IrCollectionConstruction.ComparerOnly
                ? (creation as IrNewExpression)?.Arguments[0].Value
                : null,
            elements
        );
    }

    /// <summary>
    /// The created collection when it is one whose initializer lowers to a
    /// factory (a <c>List</c>, <c>HashSet</c> or <c>Dictionary</c> created by
    /// an object creation): its symbol, its IR type and the creation;
    /// <c>null</c> for any other creation.
    /// </summary>
    private (
        INamedTypeSymbol Symbol,
        IrCollectionTypeRef Type,
        IObjectCreationOperation Creation
    )? FactoryCollectionOf(ITypeSymbol? createdType, InitializerExpressionSyntax initializer) =>
        createdType is INamedTypeSymbol named
        && HasInitializerFactory(named)
        && MapNonNullType(named) is IrCollectionTypeRef type
        && _semantic.GetOperation(initializer.Parent!) is IObjectCreationOperation operation
            ? (named, type, operation)
            : null;

    private static bool HasInitializerFactory(INamedTypeSymbol type) =>
        CollectionKindResolver.KindOf(type)
            is IrCollectionKind.List
                or IrCollectionKind.HashSet
                or IrCollectionKind.Dictionary;

    /// <summary>
    /// The values of each <c>Add</c> element, or <c>null</c> when an element
    /// binds anything but the collection's own <c>Add</c>.
    /// </summary>
    private List<IReadOnlyList<IrExpression>>? AddElementsOf(
        InitializerExpressionSyntax initializer,
        INamedTypeSymbol collection
    )
    {
        foreach (var element in initializer.Expressions)
        {
            var add = _semantic.GetCollectionInitializerSymbolInfo(element).Symbol as IMethodSymbol;

            var isOwnAdd =
                IsInstanceAdd(add)
                && SymbolEqualityComparer.Default.Equals(
                    add.ContainingType.OriginalDefinition,
                    collection.OriginalDefinition
                );

            if (!isOwnAdd)
                return null;
        }

        return
        [
            .. initializer.Expressions.Select(element =>
                (IReadOnlyList<IrExpression>)[.. AddArguments(element).Select(Extract)]
            ),
        ];
    }

    /// <summary>
    /// The key and value of each <c>[k] = v</c> element of a dictionary, or
    /// <c>null</c> when an element is anything else (a member, a nested
    /// initializer, several indices, a list).
    /// </summary>
    private List<IReadOnlyList<IrExpression>>? AssignElementsOf(
        InitializerExpressionSyntax initializer,
        IrCollectionTypeRef type
    )
    {
        if (type.Kind is not IrCollectionKind.Dictionary)
            return null;

        var isPlainIndexWrite = initializer.Expressions.All(element =>
            element
                is AssignmentExpressionSyntax
                {
                    Left: ImplicitElementAccessSyntax { ArgumentList.Arguments.Count: 1 },
                    Right: not InitializerExpressionSyntax,
                }
        );

        if (!isPlainIndexWrite)
            return null;

        return
        [
            .. initializer
                .Expressions.Cast<AssignmentExpressionSyntax>()
                .Select(element =>
                    (IReadOnlyList<IrExpression>)
                        [
                            Extract(
                                ((ImplicitElementAccessSyntax)element.Left)
                                    .ArgumentList
                                    .Arguments[0]
                                    .Expression
                            ),
                            Extract(element.Right),
                        ]
                ),
        ];
    }

    private static IrCollectionConstruction ConstructionOf(IMethodSymbol? constructor) =>
        constructor?.Parameters switch
        {
            [] => IrCollectionConstruction.Parameterless,
            [{ Type: INamedTypeSymbol { Name: "IEqualityComparer" } }] =>
                IrCollectionConstruction.ComparerOnly,
            _ => IrCollectionConstruction.Other,
        };

    /// <summary>
    /// A call to a C# 13 <c>params</c> collection parameter (<c>params
    /// List&lt;T&gt; xs</c>, not an array or span), for a target whose collections
    /// are runtime classes: in the expanded form (<c>F(a, b)</c>) the call site
    /// builds the collection C# builds, an <see cref="IrArrayLiteral"/> with
    /// its <see cref="IrArrayLiteral.TargetCollection"/> (a <c>List</c>, a
    /// read-only adopted array, a plain array for <c>IEnumerable&lt;T&gt;</c>)
    /// or a <c>HashSet</c> construction; the normal form (<c>F(xs)</c>) passes
    /// the collection unchanged. Returns whether the parameter is such a
    /// collection (the caller then applies no array spread).
    /// </summary>
    private bool TryConstructParamsCollection(
        List<IrArgument> args,
        IMethodSymbol symbol,
        SeparatedSyntaxList<ArgumentSyntax> syntaxArgs
    )
    {
        var parameter = symbol.Parameters[^1];

        if (MapNonNullType(parameter.Type) is not (IrCollectionTypeRef or IrIterableTypeRef))
            return false;

        var first = symbol.Parameters.Length - 1;

        var isNormalForm =
            syntaxArgs.Count == symbol.Parameters.Length
            && SymbolEqualityComparer.Default.Equals(
                _semantic.GetTypeInfo(syntaxArgs[^1].Expression).ConvertedType,
                parameter.Type
            );

        if (isNormalForm)
            return true;

        // A call with named arguments binds through its bound arguments
        // (BoundArgumentsOf), which build the collection.
        if (syntaxArgs.Any(argument => argument.NameColon is not null))
            return true;

        var elements = args.Skip(first).Select(argument => argument.Value).ToList();

        args.RemoveRange(first, args.Count - first);
        args.Add(new IrArgument(ParamsCollectionOf(parameter, elements)));

        return true;
    }

    /// <summary>
    /// The collection C# builds for a <c>params</c> collection parameter from
    /// <paramref name="elements"/>: a <c>HashSet</c> construction, or an
    /// <see cref="IrArrayLiteral"/> with the collection it targets.
    /// </summary>
    private IrExpression ParamsCollectionOf(
        IParameterSymbol parameter,
        IReadOnlyList<IrExpression> elements
    )
    {
        var literal = new IrArrayLiteral(elements);

        return
            parameter.Type is INamedTypeSymbol named
            && CollectionKindResolver.KindOf(named) is IrCollectionKind.HashSet
            ? new IrNewExpression(
                MapNonNullType(named),
                elements.Count == 0 ? [] : [new IrArgument(literal)]
            )
            : literal with
            {
                TargetCollection = TargetCollectionOf(parameter.Type),
            };
    }

    /// <summary>
    /// <paramref name="value"/> viewed as the list interface C# converts the
    /// array <paramref name="expression"/> to implicitly (spec 011 FR-003):
    /// an <see cref="IrArrayToCollectionConversion"/>; unchanged for any other
    /// expression.
    /// </summary>
    private IrExpression WithArrayToCollectionConversion(
        ExpressionSyntax expression,
        IrExpression value
    )
    {
        var info = _semantic.GetTypeInfo(expression);

        return ArrayToCollectionConversionOf(value, info.Type, info.ConvertedType) ?? value;
    }

    /// <summary>
    /// <paramref name="value"/> converted from the array <paramref name="source"/> to
    /// the list interface <paramref name="target"/>
    /// (<c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>), with the
    /// array's own element type; <c>null</c> for any other pair.
    /// </summary>
    private IrArrayToCollectionConversion? ArrayToCollectionConversionOf(
        IrExpression value,
        ITypeSymbol? source,
        ITypeSymbol? target
    )
    {
        if (
            source is not IArrayTypeSymbol array
            || target is not INamedTypeSymbol named
            || !IsListInterface(named)
            || MapNonNullType(named) is not IrCollectionTypeRef collection
        )
            return null;

        return new IrArrayToCollectionConversion(value, collection)
        {
            SourceElementType = MapNonNullType(array.ElementType),
        };
    }

    private static bool IsListInterface(INamedTypeSymbol type) =>
        CollectionKindResolver.KindOf(type)
            is IrCollectionKind.IList
                or IrCollectionKind.ICollection
                or IrCollectionKind.IReadOnlyList
                or IrCollectionKind.IReadOnlyCollection;

    /// <summary>
    /// An explicit cast to a list interface (spec 011 FR-003, data-model § 2
    /// B3): from an array it views the array as a fixed-size list; from a type
    /// that is neither an array nor a stage 011c collection (<c>object</c>,
    /// <c>IEnumerable&lt;T&gt;</c>) the value may be a native array at run
    /// time, an <see cref="IrListInterfaceCast"/>. <c>null</c> for any other
    /// cast.
    /// </summary>
    private IrExpression? TryExtractListInterfaceCast(
        CastExpressionSyntax cast,
        IrExpression inner,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    )
    {
        if (targetType is not INamedTypeSymbol named || !IsListInterface(named))
            return null;

        if (ArrayToCollectionConversionOf(inner, sourceType, targetType) is { } conversion)
            return conversion;

        if (IsStageCollectionOrImplementer(sourceType))
            return null;

        return MapNonNullType(named) is IrCollectionTypeRef collection
            ? new IrListInterfaceCast(inner, collection, ExtractTargetType(cast))
            {
                TargetClrTypeName = ClrTypeName.Of(named),
            }
            : null;
    }

    /// <summary>
    /// Whether a value of <paramref name="type"/> is never a native array: a
    /// stage 011c collection, a class deriving from one, or a class or struct
    /// implementing a list interface itself.
    /// </summary>
    private static bool IsStageCollectionOrImplementer(ITypeSymbol type)
    {
        for (var named = type as INamedTypeSymbol; named is not null; named = named.BaseType)
        {
            if (CollectionKindResolver.KindOf(named) is not null)
                return true;
        }

        return type.TypeKind is TypeKind.Class or TypeKind.Struct
            && type.AllInterfaces.Any(IsListInterface);
    }

    /// <summary>
    /// <paramref name="args"/> with the empty collection C# passes for an
    /// omitted <c>params</c> collection when the call names its arguments (the
    /// positional form builds it in <see cref="TryConstructParamsCollection"/>),
    /// appended last, after every argument the source evaluates.
    /// </summary>
    private (List<IrArgument> Args, IReadOnlyList<int>? Order) WithOmittedParamsCollection(
        InvocationExpressionSyntax invocation,
        List<IrArgument> args,
        IReadOnlyList<int>? order
    )
    {
        var omitted =
            invocation.ArgumentList.Arguments.Any(argument => argument.NameColon is not null)
            && _semantic.GetOperation(invocation) is IInvocationOperation operation
                ? operation.Arguments.FirstOrDefault(argument =>
                    argument.ArgumentKind is ArgumentKind.ParamCollection
                    && argument.Value.DescendantsAndSelf().All(value => value.IsImplicit)
                )
                : null;

        if (omitted?.Parameter is not { } parameter)
            return (args, order);

        return (
            [.. args, new IrArgument(ParamsCollectionOf(parameter, []))],
            order is null ? null : [.. order, args.Count]
        );
    }
}
