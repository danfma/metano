namespace Metano.Compiler.IR;

/// <summary>
/// A constructor declaration on a class or struct.
/// </summary>
/// <param name="Parameters">Constructor parameters (may include promoted parameters
/// that become properties).</param>
/// <param name="Body">Constructor body statements.</param>
/// <param name="BaseArguments">Arguments passed to the base constructor, if any.</param>
/// <param name="Overloads">The type's other explicit constructors, whatever
/// their accessibility, when it declares more than one.</param>
/// <param name="BaseExceptionConstruction">Set when the base constructor called
/// (explicitly via <c>: base(...)</c> or implicitly) belongs to an exception type
/// with no emitted target declaration: the base arguments classified by
/// constructor-parameter role, so a backend can build the native error
/// <c>super(...)</c> call. Null otherwise.</param>
/// <param name="ThisChain">Set when the constructor delegates to a sibling
/// through <c>: this(...)</c>: the chained arguments and the target
/// constructor, so a backend without constructor chaining can inline the
/// target (and, transitively, its base call). Null otherwise.</param>
/// <param name="Visibility">The constructor's declared accessibility; a
/// primary constructor, whose accessibility the source cannot declare, and an
/// implicit one are <see cref="IrVisibility.Public"/>. Each backend maps it to
/// its own constructor access.</param>
/// <param name="IsPrimary">Whether this is a record positional or C# 12
/// primary constructor, declared by the type's parameter list: it has no body
/// of its own, its promoted and captured parameters initialize members, and
/// every other constructor of the type chains to it through
/// <see cref="ThisChain"/>.</param>
public sealed record IrConstructorDeclaration(
    IReadOnlyList<IrConstructorParameter> Parameters,
    IReadOnlyList<IrStatement>? Body = null,
    IReadOnlyList<IrArgument>? BaseArguments = null,
    IReadOnlyList<IrConstructorDeclaration>? Overloads = null,
    IrExceptionConstruction? BaseExceptionConstruction = null,
    IrConstructorChain? ThisChain = null,
    IrVisibility Visibility = IrVisibility.Public,
    bool IsPrimary = false
)
{
    /// <summary>
    /// The parameters without their promotion details, as a method declares
    /// them.
    /// </summary>
    public IReadOnlyList<IrParameter> PlainParameters =>
        Parameters.Select(p => p.Parameter).ToList();

    /// <summary>
    /// The constructor's name in its dispatched set (spec 013 FR-001), from
    /// which a target derives its selector and factory candidates;
    /// <c>null</c> when the type declares fewer than two constructors.
    /// </summary>
    public IrOverloadName? OverloadName { get; init; }

    /// <summary>
    /// The constructor's C# signature (<c>Money(decimal amount)</c>), the
    /// description of its selector symbol (spec 013 D2); <c>null</c> outside a
    /// dispatched set.
    /// </summary>
    public string? SignatureText { get; init; }

    /// <summary>
    /// The constructor's position among the type's declared constructors, in
    /// declaration order; <c>0</c> outside a dispatched set. A target emits
    /// selectors and factories in this order (spec 013 FR-006).
    /// </summary>
    public int DeclarationOrdinal { get; init; }

    /// <summary>
    /// Every constructor of a dispatched set — this one and its
    /// <see cref="Overloads"/> — in declaration order.
    /// </summary>
    public IReadOnlyList<IrConstructorDeclaration> DeclarationsInOrder =>
        new[] { this }
            .Concat(Overloads ?? [])
            .OrderBy(declaration => declaration.DeclarationOrdinal)
            .ToList();
}

/// <summary>
/// A <c>: this(...)</c> delegation: <paramref name="Arguments"/> as written at
/// the call (positional or named) and the <paramref name="Target"/>
/// constructor they bind to.
/// </summary>
public sealed record IrConstructorChain(
    IReadOnlyList<IrArgument> Arguments,
    IrConstructorDeclaration Target
);

/// <summary>
/// A constructor parameter that may be promoted to a property and/or captured
/// by a field initializer (DI-style: <c>private readonly IFoo _foo = foo;</c>).
/// Composes <see cref="IrParameter"/> with the promotion mode and an optional
/// captured-field marker so backends can synthesize the matching
/// <c>this._foo = foo</c> assignment in the constructor body without
/// re-walking the type's fields.
/// </summary>
/// <param name="Parameter">The underlying parameter (name, type, default value flag).</param>
/// <param name="Promotion">Whether this parameter is promoted to a property.</param>
/// <param name="CapturedFieldName">When non-null, the C# field name that
/// captures this parameter via a <c>= paramName</c> initializer. Backends
/// emit a <c>this.&lt;capturedFieldName&gt; = &lt;paramName&gt;</c>
/// assignment in the constructor body and drop the field's initializer to
/// avoid double-assignment. The name is the CLR field name in its source
/// casing — naming policy still applies on the emit side.</param>
/// <param name="PromotedVisibility">When the parameter is promoted to a
/// property (<see cref="Promotion"/> is not <see cref="IrParameterPromotion.None"/>),
/// the visibility of that promoted property — backends use it to render the
/// matching access modifier on the constructor parameter shorthand. Falls
/// back to <c>Public</c> on any unrecognised mapping at the bridge.</param>
/// <param name="EmittedName">Target-resolved <c>[Name]</c> override applied
/// to the promoted property; <c>null</c> when no override exists or when the
/// parameter isn't promoted. Backends use this in place of camelCasing the
/// underlying parameter name so the emitted member matches the property the
/// user named.</param>
public sealed record IrConstructorParameter(
    IrParameter Parameter,
    IrParameterPromotion Promotion = IrParameterPromotion.None,
    string? CapturedFieldName = null,
    IrVisibility? PromotedVisibility = null,
    string? EmittedName = null
);

/// <summary>
/// How a constructor parameter is promoted to a class member.
/// </summary>
public enum IrParameterPromotion
{
    /// <summary>Plain parameter, not promoted.</summary>
    None,

    /// <summary>Promoted to a readonly property (C# record positional parameter).</summary>
    ReadonlyProperty,

    /// <summary>Promoted to a mutable property.</summary>
    MutableProperty,
}
