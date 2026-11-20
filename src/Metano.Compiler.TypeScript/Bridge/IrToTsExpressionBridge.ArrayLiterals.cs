using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Array literals. TypeScript infers a literal's element type from its
/// elements, so a literal C# types wider
/// (<see cref="IrArrayLiteral.ElementType"/>) is asserted to that type:
/// <c>[1] as (number | null)[]</c>, <c>[] as number[]</c>; a literal a
/// collection factory wraps is typed by the factory's type arguments instead
/// (<c>List.of&lt;number | null&gt;([1])</c>). A local declared
/// with an array type annotates the local instead and keeps the literal bare
/// (see <see cref="IrToTsStatementBridge"/>).
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>The literal without the assertion of its element type.</summary>
    internal static TsArrayLiteral MapBareArrayLiteral(
        IrArrayLiteral literal,
        IrToTsBridgeContext context
    ) => new(literal.Elements.Select(element => Map(element, context)).ToList());

    private static TsExpression MapArrayLiteral(IrArrayLiteral literal, IrToTsBridgeContext context)
    {
        if (literal.TargetCollection is not { } collection)
            return MapTypedArrayLiteral(literal, context);

        var elements = MapBareArrayLiteral(literal, context);

        return
            collection.Kind
                is IrCollectionKind.List
                    or IrCollectionKind.ImmutableList
                    or IrCollectionKind.ImmutableArray
            ? CollectionFactoryCall(collection, "of", [elements], context)
            : AdoptedFixedSize(elements, collection, context);
    }

    /// <summary>
    /// <c>C.factory&lt;T…&gt;(args)</c> on the runtime class of
    /// <paramref name="collection"/>, its type arguments written from the C#
    /// type (<see cref="CollectionTypeArguments"/>).
    /// </summary>
    private static TsCallExpression CollectionFactoryCall(
        IrCollectionTypeRef collection,
        string factory,
        IReadOnlyList<TsExpression> arguments,
        IrToTsBridgeContext context
    ) =>
        (TsCallExpression)
            CollectionTypeArgumentSlots.Append(
                new TsCallExpression(
                    new TsPropertyAccess(RuntimeCollections.ReferenceTo(collection.Kind), factory),
                    arguments,
                    TypeArguments: CollectionTypeArguments(collection, context)
                ),
                collection,
                context.RuntimeTypes
            );

    /// <summary>
    /// The literal as a native array, asserted to the element type C# gives it
    /// when the elements alone would make TypeScript infer a narrower one.
    /// </summary>
    private static TsExpression MapTypedArrayLiteral(
        IrArrayLiteral literal,
        IrToTsBridgeContext context
    )
    {
        var elements = MapBareArrayLiteral(literal, context);

        if (literal.ElementType is not { } elementType)
            return elements;

        var arrayType = new TsArrayType(IrToTsTypeMapper.Map(elementType, context.TypeOverrides));

        return new TsCastExpression(elements, arrayType);
    }
}
