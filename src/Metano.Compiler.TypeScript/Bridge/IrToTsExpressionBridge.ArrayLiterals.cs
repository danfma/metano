using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Array literals. TypeScript infers a literal's element type from its
/// elements, so a literal C# types wider
/// (<see cref="IrArrayLiteral.ElementType"/>) is asserted to that type:
/// <c>[1] as (number | null)[]</c>, <c>[] as number[]</c>. A local declared
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
        var array = MapTypedArrayLiteral(literal, context);

        return literal.TargetCollection switch
        {
            null => array,
            { Kind: IrCollectionKind.List } => new TsCallExpression(
                new TsPropertyAccess(ListClass, "of"),
                [array]
            ),
            _ => AdoptedFixedSize(array),
        };
    }

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
