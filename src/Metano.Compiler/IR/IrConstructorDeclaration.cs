namespace Metano.Compiler.IR;

/// <summary>
/// A constructor declaration on a class or struct.
/// </summary>
/// <param name="Parameters">Constructor parameters (may include promoted parameters
/// that become properties).</param>
/// <param name="Body">Constructor body statements.</param>
/// <param name="BaseArguments">Arguments passed to the base constructor, if any.</param>
/// <param name="Overloads">Additional constructor overloads, if the type has multiple
/// public constructors.</param>
public sealed record IrConstructorDeclaration(
    IReadOnlyList<IrConstructorParameter> Parameters,
    IReadOnlyList<IrStatement>? Body = null,
    IReadOnlyList<IrArgument>? BaseArguments = null,
    IReadOnlyList<IrConstructorDeclaration>? Overloads = null
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
