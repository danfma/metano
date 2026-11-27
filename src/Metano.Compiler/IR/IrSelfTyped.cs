namespace Metano.Compiler.IR;

/// <summary>
/// A self-typed static method of a CRTP-shaped class (spec 001 FR-113): it needs its
/// class's self parameter only to construct it (<c>new TSelf()</c>) or to forward it
/// to another self-typed static, so a target builds it from the class the method is
/// called on.
/// </summary>
/// <param name="SelfParameterName">The class's self type parameter (<c>TSelf</c>).</param>
/// <param name="Forwards">The body calls another self-typed static through its receiver.</param>
public sealed record IrSelfTypedMember(string SelfParameterName, bool Forwards);

/// <summary>
/// The binding of the callee class's self parameter at a call to a self-typed static
/// (spec 001 FR-113 item 3): the class the call receives on, however the source spells
/// the receiver, with the facts a target needs to name the callee's declaration.
/// </summary>
/// <param name="Argument">The self argument: a class (<c>CardH</c>, <c>Card&lt;int&gt;</c>),
/// or the caller's own self parameter with <see cref="IrTypeParameterScope.Receiver"/>
/// scope when a self-typed static forwards it.</param>
/// <param name="Class">A reference to the self argument's class; <c>null</c> when the
/// call forwards through the caller's receiving class.</param>
/// <param name="SelfParameterName">The callee class's self type parameter (<c>TSelf</c>).</param>
/// <param name="ClassTypeArguments">Each type parameter of the callee's class, with its
/// constraints, and the argument this call binds it to (<c>TSelf = Card&lt;int&gt;</c> for
/// <c>Component&lt;Card&lt;int&gt;&gt;.Make()</c>).</param>
/// <param name="Parameters">The callee's declared parameter types, over its class's type
/// parameters.</param>
/// <param name="ReturnType">The callee's declared return type, over its class's type
/// parameters.</param>
public sealed record IrSelfTypeBinding(
    IrTypeRef Argument,
    IrTypeReference? Class,
    string SelfParameterName,
    IReadOnlyList<IrClassTypeArgument> ClassTypeArguments,
    IReadOnlyList<IrTypeRef> Parameters,
    IrTypeRef ReturnType
);

/// <summary>A type parameter of a class and the argument a reference binds it to.</summary>
public sealed record IrClassTypeArgument(IrTypeParameter Parameter, IrTypeRef Argument);
