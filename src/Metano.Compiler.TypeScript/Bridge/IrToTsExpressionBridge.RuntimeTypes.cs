using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Parenthesization.Templates;

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

        if (FoldedPlainObjectOf(getType, context) is { } plain)
            return FoldedPlainObjectRead(getType.Receiver, plain, context);

        return new TsCallExpression(
            new TsRuntimeHelperReference("getRuntimeType"),
            [Map(getType.Receiver, context)]
        );
    }

    /// <summary>
    /// A mapped template that reads its type arguments' descriptors
    /// (<c>$R0</c>: <c>Enum.Parse&lt;E&gt;</c> → <c>enumFromRaw(E$runtimeType, …)</c>)
    /// gets them here; a type argument with no descriptor reads the
    /// <c>UnknownType</c> wildcard, as a specialization's does (FR-023).
    /// </summary>
    private static TsExpression WithTypeArgumentDescriptors(
        TsExpression mapped,
        IrCallExpression call,
        IrToTsBridgeContext context
    ) =>
        mapped is TsTemplate template
        && TsTemplatePlaceholder.ReferencesTypeArgumentDescriptor(template.Template)
            ? template with
            {
                TypeArgumentDescriptors =
                [
                    .. (call.TypeArguments ?? []).Select(argument =>
                        IrToTsRuntimeTypeReferences.ArgumentOf(
                            argument,
                            "mapped type argument",
                            context.RuntimeTypes
                        )
                    ),
                ],
            }
            : mapped;

    /// <summary>
    /// A <c>[PlainObject]</c> receiver with no derived plain object answers its
    /// own descriptor (FR-020): a plain object has no runtime identity to ask.
    /// </summary>
    private static IrNamedTypeRef? FoldedPlainObjectOf(
        IrGetTypeExpression getType,
        IrToTsBridgeContext context
    ) =>
        getType.ReceiverType is IrNamedTypeRef { Semantics.IsPlainObject: true } plain
        && context.RuntimeTypes.FoldedPlainObjects.Contains(plain.SupertypeKey)
            ? plain
            : null;

    /// <summary>
    /// <c>Dto$runtimeType</c> for a receiver that is neither <c>null</c> nor
    /// has an effect to keep (<c>this</c>, a <c>new</c> of side-effect-free
    /// arguments). Any other receiver still runs, and still throws
    /// <c>NullReferenceException</c> on <c>null</c> as .NET does:
    /// <c>(getRuntimeType(dto), Dto$runtimeType)</c>.
    /// </summary>
    private static TsExpression FoldedPlainObjectRead(
        IrExpression receiver,
        IrNamedTypeRef plain,
        IrToTsBridgeContext context
    )
    {
        var descriptor = IrToTsRuntimeTypeReferences.Of(plain, "GetType()", context.RuntimeTypes)!;

        if (IsPureNonNull(receiver))
            return descriptor;

        var nullChecked = new TsCallExpression(
            new TsRuntimeHelperReference("getRuntimeType"),
            [Map(receiver, context)]
        );

        return new TsParenthesized(new TsBinaryExpression(nullChecked, ",", descriptor));
    }

    private static bool IsPureNonNull(IrExpression receiver) =>
        receiver switch
        {
            IrThisExpression => true,
            IrNewExpression construction => construction.Arguments.All(argument =>
                IrEvaluationTraits.IsSideEffectFree(argument.Value)
            )
                && (construction.Initializers ?? []).All(init =>
                    IrEvaluationTraits.IsSideEffectFree(init.Value)
                ),
            _ => false,
        };

    /// <summary>
    /// <c>typeof(T)</c>: the type's descriptor, or the throwing placeholder for
    /// a type the reachability phase reported as having none.
    /// </summary>
    private static TsExpression MapTypeOf(IrTypeOfExpression typeOf, IrToTsBridgeContext context) =>
        IrToTsRuntimeTypeReferences.Of(typeOf.Type, "typeof", context.RuntimeTypes)
        ?? MapUnsupported(new IrUnsupportedExpression("TypeOfExpression"));
}
