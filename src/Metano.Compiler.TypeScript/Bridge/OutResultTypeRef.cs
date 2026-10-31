using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript result type of a lowered method with <c>out</c> parameters
/// (see <see cref="OutParameterLowering"/>), a labeled readonly tuple:
/// <c>readonly [result: R, value: T]</c> (<c>void</c> drops the result slot).
/// For a <c>bool</c> result with at least one <c>out</c> parameter carrying
/// <see cref="IrParameter.OutTypesByResult"/>, a union of one tuple per result
/// value, so destructuring with <c>const</c> narrows the values on the result:
/// <c>readonly [found: true, value: V] | readonly [found: false, value: V | null]</c>.
/// TypeScript-only: it exists between <see cref="OutParameterLowering"/> and
/// <see cref="IrToTsTypeMapper"/>.
/// </summary>
internal sealed record OutResultTypeRef(
    IrTypeRef? Result,
    string ResultLabel,
    IReadOnlyList<IrParameter> Outs
) : IrTypeRef, IIrCompositeTypeRef
{
    public bool IsDiscriminated =>
        Result is IrPrimitiveTypeRef { Primitive: IrPrimitive.Boolean }
        && Outs.Any(o => o.OutTypesByResult is not null);

    /// <summary>The types the tuple's referenced types and type parameters come from.</summary>
    public IEnumerable<IrTypeRef> ComponentTypes =>
        Outs.SelectMany(o =>
                o.OutTypesByResult is { } byResult
                    ? new[] { byResult.WhenTrue, byResult.WhenFalse }
                    : [o.Type]
            )
            .Concat(Result is null ? [] : [Result]);

    public static OutResultTypeRef For(IrTypeRef? result, IReadOnlyList<IrParameter> outs)
    {
        // The core names its result temporaries `$found` / `$result` after the
        // same rule (OutArgumentCallLowering.ResultSlot); a `$` prefix here keeps
        // the label clear of an out parameter with the same name.
        var label = result is IrPrimitiveTypeRef { Primitive: IrPrimitive.Boolean }
            ? "found"
            : "result";

        while (outs.Any(o => o.Name == label))
            label = "$" + label;

        return new OutResultTypeRef(result, label, outs);
    }

    /// <summary>
    /// The type of the local an <c>out</c> parameter becomes in the body: wide
    /// enough for the value on every result.
    /// </summary>
    public static IrTypeRef LocalTypeOf(IrParameter parameter) =>
        parameter.OutTypesByResult switch
        {
            { WhenTrue: IrNullableTypeRef whenTrue } => whenTrue,
            { WhenFalse: IrNullableTypeRef whenFalse } => whenFalse,
            _ => parameter.Type,
        };
}
