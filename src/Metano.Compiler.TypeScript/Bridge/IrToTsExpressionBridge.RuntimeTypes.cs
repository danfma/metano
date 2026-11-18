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
    /// a primitive with a descriptor; every other target (a class, which keeps
    /// <c>x instanceof C ? x : null</c> and reads no descriptor, D29) lowers
    /// through the conditional that defines it.
    /// </summary>
    private static TsExpression MapAsCast(
        IrConditionalExpression asCast,
        IrToTsBridgeContext context
    ) =>
        AsTypeDescriptorOf(asCast, context) is { } descriptor
            ? AsTypeCall(Map(AsOperandOf(asCast)!, context), descriptor)
            : new TsConditionalExpression(
                Map(asCast.Condition, context),
                Map(asCast.WhenTrue, context),
                Map(asCast.WhenFalse, context)
            );

    /// <summary>
    /// An <c>as</c> whose operand extraction bound once
    /// (<c>let $v = x in $v is T ? $v : null</c>): <c>asType</c> evaluates its
    /// argument once itself, so the binding is dropped,
    /// <c>asType(x, descriptor)</c>, with no immediately invoked function (D4).
    /// <c>null</c> for any other binding.
    /// </summary>
    private static TsExpression? TryMapBoundAsCast(
        IrLetExpression let,
        IrToTsBridgeContext context
    ) =>
        let.Body is IrConditionalExpression { AsTarget: not null } asCast
        && AsOperandOf(asCast) is IrIdentifier { Name: var bound }
        && bound == let.Name
        && AsTypeDescriptorOf(asCast, context) is { } descriptor
            ? AsTypeCall(Map(let.Value, context), descriptor)
            : null;

    private static IrExpression? AsOperandOf(IrConditionalExpression asCast) =>
        asCast.Condition is IrIsPatternExpression { Pattern: IrTypePattern } test
            ? test.Expression
            : null;

    private static TsExpression? AsTypeDescriptorOf(
        IrConditionalExpression asCast,
        IrToTsBridgeContext context
    ) =>
        asCast.Condition is IrIsPatternExpression { Pattern: IrTypePattern pattern }
        && IrRuntimeTypeTest.Of(pattern.Type, pattern.InputType)
            is IrRuntimeTypeTest.NominalInterface
                or IrRuntimeTypeTest.Primitive
            ? IrToTsRuntimeTypeReferences.Of(pattern.Type, "as", context.RuntimeTypes)
            : null;

    private static TsCallExpression AsTypeCall(TsExpression value, TsExpression descriptor) =>
        new(new TsRuntimeHelperReference("asType"), [value, descriptor]);

    /// <summary>
    /// <c>x.GetType()</c>: the folded descriptor of a primitive, enum or struct
    /// receiver, otherwise <c>getRuntimeType(x)</c>.
    /// </summary>
    private static TsExpression MapGetType(IrGetTypeExpression getType, IrToTsBridgeContext context)
    {
        if (
            getType.ConstantType is { } constant
            && IrToTsRuntimeTypeReferences.Of(constant, "GetType()", context.RuntimeTypes)
                is { } folded
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
    private static TsExpression MapTypeOf(IrTypeOfExpression typeOf, IrToTsBridgeContext context) =>
        IrToTsRuntimeTypeReferences.Of(typeOf.Type, "typeof", context.RuntimeTypes)
        ?? MapUnsupported(new IrUnsupportedExpression("TypeOfExpression"));
}
