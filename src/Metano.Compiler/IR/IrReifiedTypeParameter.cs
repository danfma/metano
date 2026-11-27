namespace Metano.Compiler.IR;

/// <summary>
/// A type parameter that is reified at runtime via a hidden <c>RuntimeType</c>
/// parameter or class slot (spec 011 FR-024, stage 011d).
/// </summary>
/// <param name="Name">The C# type parameter name (e.g. <c>T</c>).</param>
/// <param name="HiddenParameterName">The emitted hidden parameter name (e.g. <c>T$type</c>).</param>
/// <param name="Position">0-based index in the emitted parameter list after all non-<c>params</c> declared parameters and before any <c>params</c> rest parameter.</param>
/// <param name="HasDefault"><c>true</c> when a preceding declared parameter is optional, so the hidden parameter defaults to <c>UnknownType</c>.</param>
public sealed record IrReifiedTypeParameter(
    string Name,
    string HiddenParameterName,
    int Position,
    bool HasDefault
);

/// <summary>
/// Where a reified type parameter's <c>RuntimeType</c> value is resolved at runtime.
/// </summary>
public enum IrTypeParameterScope
{
    /// <summary>The type parameter is not reified in this scope.</summary>
    None,

    /// <summary>Resolved from the method, module function, or lambda's hidden <c>T$type</c> parameter.</summary>
    Method,

    /// <summary>Resolved from the instance's per-declaring-class slot (<c>this.$typeArguments$&lt;Class&gt;[i]</c>).</summary>
    ClassSlot,

    /// <summary>Resolved from the constructor's trailing <c>$typeArgs[i]</c> parameter.</summary>
    CtorParam,

    /// <summary>
    /// The self parameter of a self-typed static member (spec 001 FR-113): the receiver
    /// class the member is called on, which <c>new TSelf()</c> constructs.
    /// </summary>
    Receiver,
}
