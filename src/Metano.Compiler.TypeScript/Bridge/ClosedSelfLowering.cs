using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The closed type a class emission lowers when its family is a union (spec 010
/// FR-C5, #738): the module-level name the alias and the value binding take, the
/// runtime class that carries the members, and the union type (with the class's type
/// parameters) a <c>this</c> read as a value is asserted to.
/// </summary>
/// <param name="FamilyName">The module-level name of the closed type
/// (<c>JobStatus</c>), which the union alias and the value binding take.</param>
/// <param name="RuntimeClassName">The renamed runtime class
/// (<c>JobStatus$Base</c>).</param>
/// <param name="FamilyType">The union alias applied to the class's own type
/// parameters (<c>Tree&lt;T&gt;</c>).</param>
internal sealed record ClosedSelfLowering(
    string FamilyName,
    string RuntimeClassName,
    TsType FamilyType
)
{
    /// <summary>
    /// <paramref name="self"/> (the runtime class's <c>this</c>) asserted to the union.
    /// The assertion goes through <c>unknown</c>: a case that overrides a protected
    /// member (a record's <c>$printMembers</c>) is not comparable with the runtime
    /// class, so a direct <c>this as X</c> is rejected (TS2352).
    /// </summary>
    public TsExpression AssertFamily(TsExpression self) =>
        new TsCastExpression(new TsCastExpression(self, new TsNamedType("unknown")), FamilyType);

    /// <summary>
    /// <c>this</c> read as a value: inside the runtime class it has the class's type,
    /// which the union does not accept, so it is asserted to the union.
    /// </summary>
    public static TsExpression ThisValue(IrToTsBridgeContext context)
    {
        var self = new TsIdentifier(context.ThisReceiver);

        return context.ClosedSelf is { } closed && context.ThisReceiver == "this"
            ? closed.AssertFamily(self)
            : self;
    }

    /// <summary>
    /// The closed type's own name in value position (<c>JobStatus.make()</c>, a static
    /// initializer) names the runtime class: the value binding is declared after the
    /// class and is still uninitialized while the class body evaluates.
    /// </summary>
    public static TsExpression RuntimeClassValue(
        TsExpression reference,
        ClosedSelfLowering? closed
    ) =>
        closed is not null
        && RootOf(reference) is TsIdentifier { Name: var name } root
        && name == closed.FamilyName
            ? Replace(reference, root, new TsIdentifier(closed.RuntimeClassName))
            : reference;

    /// <summary>
    /// The receiver of a protected member read through a value typed as a closed type
    /// (<c>other.Rank()</c> with <c>other: Command</c>), upcast to the runtime class:
    /// TypeScript hides a protected member on the union of the cases. C# admits such a
    /// read only inside the family (CS1540), where the receiver's cases all derive from
    /// the runtime class being emitted.
    /// </summary>
    public static TsExpression ProtectedReceiver(
        TsExpression receiver,
        IrMemberAccess access,
        ClosedSelfLowering? closed
    ) =>
        closed is not null
        && access
            is { Origin.IsProtected: true, TargetType: IrNamedTypeRef { Semantics.IsClosed: true } }
        && access.Target is not (IrThisExpression or IrBaseExpression)
            ? new TsCastExpression(receiver, closed.RuntimeClassType)
            : receiver;

    /// <summary>The runtime class applied to the class's own type parameters.</summary>
    private TsNamedType RuntimeClassType =>
        new(RuntimeClassName, (FamilyType as TsNamedType)?.TypeArguments);

    private static TsExpression RootOf(TsExpression reference) =>
        reference is TsPropertyAccess access ? RootOf(access.Object) : reference;

    private static TsExpression Replace(
        TsExpression reference,
        TsExpression root,
        TsExpression replacement
    ) =>
        reference switch
        {
            _ when ReferenceEquals(reference, root) => replacement,
            TsPropertyAccess access => access with
            {
                Object = Replace(access.Object, root, replacement),
            },
            _ => reference,
        };
}
