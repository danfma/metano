using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The TypeScript shape of a self-typed static of a CRTP-shaped class (spec 001
/// FR-113): the method types its receiver (<c>this: new () =&gt; TSelf</c>) and
/// constructs it (<c>new this()</c>), and every call is emitted on the class bound to
/// the self parameter (<c>CardH.use(…)</c>), or on <c>this</c> when a self-typed
/// static forwards its own.
/// </summary>
internal static class SelfTypedStatics
{
    private const string ThisName = "this";

    /// <summary>
    /// <paramref name="parameters"/> with the <c>this</c> parameter of a self-typed
    /// <paramref name="method"/> of class <paramref name="className"/> in front.
    /// </summary>
    public static IReadOnlyList<TsParameter> WithThisParameter(
        IReadOnlyList<TsParameter> parameters,
        IrMethodDeclaration method,
        string className
    ) =>
        method.SelfTyped is { } self
            ? [new TsParameter(ThisName, ReceiverType(self, className)), .. parameters]
            : parameters;

    /// <summary>
    /// <paramref name="parameters"/> of an overload signature with the <c>this</c>
    /// parameter of a self-typed <paramref name="method"/> in front: the signature types
    /// the receiver a caller passes, never the static side its body forwards through.
    /// </summary>
    public static IReadOnlyList<TsParameter> WithSignatureThisParameter(
        IReadOnlyList<TsParameter> parameters,
        IrMethodDeclaration method
    ) =>
        method.SelfTyped is { } self
            ?
            [
                new TsParameter(ThisName, ConstructorOf(new TsNamedType(self.SelfParameterName))),
                .. parameters,
            ]
            : parameters;

    /// <summary>
    /// <paramref name="parameters"/> of a member that forwards to a self-typed
    /// <paramref name="method"/>'s fast path through <c>this</c>: its receiver also
    /// declares the static side of <paramref name="className"/> it forwards through.
    /// </summary>
    public static IReadOnlyList<TsParameter> WithForwarderThisParameter(
        IReadOnlyList<TsParameter> parameters,
        IrMethodDeclaration method,
        string className
    ) =>
        method.SelfTyped is { } self
            ?
            [
                new TsParameter(ThisName, ReceiverType(self with { Forwards = true }, className)),
                .. parameters,
            ]
            : parameters;

    /// <summary>
    /// The <c>this</c> parameter of a dispatcher over a group with a self-typed member,
    /// which forwards every branch through its receiver
    /// (<c>this: (new () =&gt; any) &amp; Omit&lt;typeof Component, "prototype"&gt;</c>).
    /// </summary>
    public static TsParameter DispatcherThisParameter(string className) =>
        new(
            ThisName,
            new TsIntersectionType([ConstructorOf(new TsAnyType()), StaticMembersOf(className)])
        );

    /// <summary>The receiver a self-typed static forwards through.</summary>
    public static TsExpression Receiver() => new TsIdentifier(ThisName);

    /// <summary>
    /// <c>new () =&gt; TSelf</c>, intersected with the class's static members when the
    /// body forwards through <c>this</c>. The static side is read through
    /// <c>Omit&lt;typeof Component, "prototype"&gt;</c>: the mapped type drops the abstract
    /// construct signature, which would reject <c>new this()</c> (TS2511), and the
    /// prototype, whose <c>Component&lt;any&gt;</c> members a derived class need not match.
    /// </summary>
    public static TsType ReceiverType(IrSelfTypedMember self, string className)
    {
        var constructor = ConstructorOf(new TsNamedType(self.SelfParameterName));

        return self.Forwards
            ? new TsIntersectionType([constructor, StaticMembersOf(className)])
            : constructor;
    }

    /// <summary><c>new this()</c>: constructs the receiver class.</summary>
    public static TsExpression NewReceiver() => new TsNewExpression(Receiver(), []);

    /// <summary>
    /// <paramref name="call"/> emitted on its self argument's class: the receiver of the
    /// member access is the class (or <c>this</c> when forwarding), and a generic class
    /// passes the class and method type arguments, which TypeScript cannot infer from
    /// <c>this</c>. Any other call is returned unchanged.
    /// </summary>
    public static IrCallExpression OnSelfClass(IrCallExpression call)
    {
        if (call is not { SelfType: { } self, Target: IrMemberAccess access })
            return call;

        var receiver = (IrExpression?)self.Class ?? new IrThisExpression();
        var onSelf = call with { Target = access with { Target = receiver }, SelfType = null };

        return
            self.Argument is IrNamedTypeRef { TypeArguments.Count: > 0 }
            && DeclaredClassTypeArguments(self) is { } classTypeArguments
            ? onSelf with
            {
                TypeArguments = [.. classTypeArguments, .. call.TypeArguments ?? []],
                HasExplicitTypeArguments = true,
            }
            : onSelf;
    }

    /// <summary>
    /// The arguments of the class type parameters the callee's TypeScript declaration
    /// declares, by the rule its emission follows
    /// (<see cref="IrTypeParameterReferences.DeclaredClassTypeParameters"/>). They are known
    /// only when the signature names the self parameter: a referenced callee may not be
    /// self-typed, and then declares the self parameter only when its signature names it.
    /// Otherwise <c>null</c>: nothing depends on the self type, and TypeScript infers it
    /// from <c>this</c>.
    /// </summary>
    private static List<IrTypeRef>? DeclaredClassTypeArguments(IrSelfTypeBinding self)
    {
        IrTypeRef[] signature = [self.ReturnType, .. self.Parameters];
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in signature)
            IrTypeParameterReferences.Collect(type, named);

        if (!named.Contains(self.SelfParameterName))
            return null;

        var declared = IrTypeParameterReferences
            .DeclaredClassTypeParameters(
                [.. self.ClassTypeArguments.Select(binding => binding.Parameter)],
                signature,
                [self.SelfParameterName]
            )
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. self
                .ClassTypeArguments.Where(binding => declared.Contains(binding.Parameter.Name))
                .Select(binding => binding.Argument),
        ];
    }

    private static TsFunctionType ConstructorOf(TsType instance) =>
        new([], instance, IsConstructor: true);

    private static TsNamedType StaticMembersOf(string className) =>
        new("Omit", [new TsTypeQuery(className), new TsStringLiteralType("prototype")]);
}
