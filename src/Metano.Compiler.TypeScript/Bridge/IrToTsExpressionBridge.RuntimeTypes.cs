using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The runtime-type reads of a body (spec 011 FR-018, FR-025, FR-026):
/// <c>as</c> through a descriptor, <c>GetType()</c> and <c>typeof</c>.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    /// <summary>
    /// <c>x as T</c>: <c>asType(x, descriptor)</c> for an interface, an enum or
    /// a primitive with a descriptor, which evaluates <c>x</c> once; every other
    /// target (a class, which keeps <c>x instanceof C ? x : null</c> and reads
    /// no descriptor, D29; a <c>decimal</c>, whose descriptor types its value as
    /// an <c>object</c>) lowers through the conditional that defines it.
    /// </summary>
    private static TsExpression MapAsCast(
        IrConditionalExpression asCast,
        IrToTsBridgeContext context
    )
    {
        if (
            asCast.Condition is IrIsPatternExpression { Pattern: IrTypePattern pattern } test
            && IrRuntimeTypeTest.Of(pattern.Type, pattern.InputType)
                is IrRuntimeTypeTest.NominalInterface
                    or IrRuntimeTypeTest.Primitive { PrimitiveKind: not IrPrimitive.Decimal }
            && IrToTsRuntimeTypeReferences.Of(pattern.Type, "as") is { } descriptor
        )
        {
            return new TsCallExpression(
                new TsRuntimeHelperReference("asType"),
                [Map(test.Expression, context), descriptor]
            );
        }

        return new TsConditionalExpression(
            Map(asCast.Condition, context),
            Map(asCast.WhenTrue, context),
            Map(asCast.WhenFalse, context)
        );
    }

    /// <summary>
    /// <c>x.GetType()</c>: the folded descriptor of a primitive, enum or struct
    /// receiver, otherwise <c>getRuntimeType(x)</c>.
    /// </summary>
    private static TsExpression MapGetType(IrGetTypeExpression getType, IrToTsBridgeContext context)
    {
        if (
            getType.ConstantType is { } constant
            && IrToTsRuntimeTypeReferences.Of(constant, "GetType()") is { } folded
        )
            return folded;

        return new TsCallExpression(
            new TsRuntimeHelperReference("getRuntimeType"),
            [Map(getType.Receiver, context)]
        );
    }

    /// <summary>
    /// <c>typeof(T)</c>: the type's descriptor, or the throwing placeholder for
    /// a type the reachability phase reported as having none.
    /// </summary>
    private static TsExpression MapTypeOf(IrTypeOfExpression typeOf) =>
        IrToTsRuntimeTypeReferences.Of(typeOf.Type, "typeof")
        ?? MapUnsupported(new IrUnsupportedExpression("TypeOfExpression"));
}
