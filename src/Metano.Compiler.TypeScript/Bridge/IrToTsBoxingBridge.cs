using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript boxes of spec 011 FR-050 / FR-051. A boxing conversion of a
/// type with a runtime descriptor (an enum TypeScript describes) renders
/// <c>box(value, E$runtimeType)</c>; a type without one (a BCL, <c>[Import]</c>
/// or <c>[NoEmit]</c> enum, an <c>[InlineWrapper]</c>) stays raw, and so does
/// every value under <c>[assembly: DisableBoxing]</c>. A value crossing an
/// interop boundary renders <c>unbox(value)</c> only when the compilation boxes
/// somewhere (the gate of FR-051).
/// </summary>
internal static class IrToTsBoxingBridge
{
    /// <summary>The box of <paramref name="box"/>, or its raw value.</summary>
    public static TsExpression MapBox(IrBoxExpression box, IrToTsBridgeContext context)
    {
        var value = IrToTsExpressionBridge.Map(box.Value, context);

        if (DescriptorOf(box.BoxedType, context) is not { } descriptor)
            return value;

        if (!box.IsLifted)
            return Helper("box", value, descriptor);

        return value is TsIdentifier
            ? new TsConditionalExpression(
                new TsBinaryExpression(value, "===", new TsLiteral("null")),
                new TsLiteral("null"),
                Helper("box", value, descriptor)
            )
            : Helper("boxNullable", value, descriptor);
    }

    /// <summary>
    /// The descriptor a box of <paramref name="type"/> carries; <c>null</c> when
    /// boxing is off or the type has none, so the value stays raw.
    /// </summary>
    public static TsExpression? DescriptorOf(IrTypeRef type, IrToTsBridgeContext context) =>
        context.RuntimeTypes.BoxingEnabled && IsBoxable(type, context)
            ? IrToTsRuntimeTypeReferences.Of(type, "boxing conversion", context.RuntimeTypes)
            : null;

    private static bool IsBoxable(IrTypeRef type, IrToTsBridgeContext context) =>
        type
            is IrNamedTypeRef
            {
                Semantics.Kind: IrNamedTypeKind.NumericEnum or IrNamedTypeKind.StringEnum,
            } named
        && IrToTsRuntimeTypeReferences.IsDescribable(named, context.RuntimeTypes);

    /// <summary>
    /// <c>unbox(value)</c> at an interop boundary when the compilation boxes;
    /// the raw value otherwise.
    /// </summary>
    public static TsExpression MapUnbox(IrUnboxExpression unbox, IrToTsBridgeContext context)
    {
        var value = IrToTsExpressionBridge.Map(unbox.Value, context);

        return context.RuntimeTypes.UnboxesAtInterop ? Helper("unbox", value) : value;
    }

    /// <summary>
    /// A concatenation operand that may hold a box: <c>String(value)</c> when the
    /// compilation boxes, so a box appends its .NET text (JS <c>+</c> would read
    /// its <c>valueOf()</c>, the raw value); the value itself otherwise.
    /// </summary>
    public static TsExpression MapConcatenatedValue(
        IrConcatenatedValue concatenated,
        IrToTsBridgeContext context
    )
    {
        var value = IrToTsExpressionBridge.Map(concatenated.Value, context);

        return context.RuntimeTypes.UnboxesAtInterop
            ? new TsCallExpression(new TsIdentifier("String"), [value])
            : value;
    }

    private static TsCallExpression Helper(string name, params TsExpression[] arguments) =>
        new(new TsRuntimeHelperReference(name), arguments);
}
