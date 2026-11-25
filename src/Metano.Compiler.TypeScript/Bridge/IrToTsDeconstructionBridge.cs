using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrTupleDeconstruction"/> to a destructuring
/// declaration: parts read by position become an array pattern
/// (<c>const [a, , [b, c]] = value</c>), parts read by member an object
/// pattern (<c>const { x: a, y: b } = point</c>).
/// </summary>
internal static class IrToTsDeconstructionBridge
{
    public static TsDestructuringDeclaration Map(
        IrTupleDeconstruction deconstruction,
        IrToTsBridgeContext context
    ) =>
        new(
            PatternOf(deconstruction.Elements, deconstruction.Members),
            deconstruction.Initializer is IrOutArgumentCall outCall
                ? IrToTsExpressionBridge.MapOutArgumentCallValue(outCall, context)
                : IrToTsExpressionBridge.Map(deconstruction.Initializer, context),
            Const: deconstruction.IsConst
        );

    private static TsBindingTarget PatternOf(
        IReadOnlyList<IrDeconstructionElement> elements,
        IReadOnlyList<IrPositionalElementMember>? members
    ) =>
        members is null
            ? new TsArrayBindingPattern(elements.Select(TargetOf).ToList())
            : new TsObjectBindingPattern(
                elements
                    .Zip(members, (element, member) => (Target: TargetOf(element), member))
                    .Where(pair => pair.Target is not null)
                    .Select(pair => new TsObjectBindingProperty(
                        IrToTsExpressionBridge.ReadDataMemberName(
                            pair.member.Name,
                            pair.member.EmittedName,
                            pair.member.IsTranspiledDataMember
                        ),
                        pair.Target!
                    ))
                    .ToList()
            );

    /// <summary>The target one element binds; <c>null</c> for a discard.</summary>
    private static TsBindingTarget? TargetOf(IrDeconstructionElement element) =>
        element switch
        {
            { Nested: { } nested } => PatternOf(nested.Elements, nested.Members),
            { Name: { } name } => new TsBindingName(IrToTsNamingPolicy.ToLocalName(name)),
            _ => null,
        };
}
