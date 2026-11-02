namespace Metano.Compiler.IR;

/// <summary>
/// Describes the semantic nature of a property independent of how any target
/// renders it, and independent of whether any expression/statement bodies have
/// been extracted yet. Backends read these flags to decide their lowering strategy.
/// </summary>
/// <param name="HasGetterBody">The getter is computed (expression-bodied or block-bodied),
/// not a plain auto-accessor. The actual body expression lands on
/// <see cref="IrPropertyDeclaration.GetterBody"/> when expression extraction is available.</param>
/// <param name="HasSetterBody">The setter has a custom body (block or expression-bodied).</param>
/// <param name="HasInitializer">The declaration includes an <c>= value</c> initializer.
/// The expression lands on <see cref="IrPropertyDeclaration.Initializer"/> when available.</param>
/// <param name="IsAbstract">Declared <c>abstract</c>.</param>
/// <param name="IsVirtual">Declared <c>virtual</c>.</param>
/// <param name="IsOverride">Declared <c>override</c>.</param>
/// <param name="IsSealed">Declared <c>sealed override</c>.</param>
/// <param name="IsRequired">Declared <c>required</c>: every object creation sets it
/// through an object initializer (or a <c>[SetsRequiredMembers]</c> constructor).</param>
/// <param name="OwnsPerClassStorage">An auto-property of an override chain the compilation
/// declares (<c>virtual</c> and overridden, or an override of a declared non-abstract
/// property). C# keeps a separate private backing field per declaring class, so a backend
/// that lowers an auto-property to one field named after it must keep the storage apart
/// per class instead (the shared field would hide a derived accessor and a
/// <c>base.P</c> read).</param>
/// <param name="InheritedAccessor">The accessor of the overridden property, declared in the compilation, that this override leaves to its base.</param>
public sealed record IrPropertySemantics(
    bool HasGetterBody = false,
    bool HasSetterBody = false,
    bool HasInitializer = false,
    bool IsAbstract = false,
    bool IsVirtual = false,
    bool IsOverride = false,
    bool IsSealed = false,
    bool IsRequired = false,
    bool OwnsPerClassStorage = false,
    IrInheritedAccessor InheritedAccessor = IrInheritedAccessor.None
);

/// <summary>
/// An accessor of an overridden property that an override leaves to its base
/// (<see cref="IrPropertySemantics.InheritedAccessor"/>). C# lets an override declare
/// only one accessor and inherits the other; a target whose accessor declaration
/// replaces both (JavaScript) must forward the missing one to the base.
/// </summary>
public enum IrInheritedAccessor
{
    /// <summary>The override declares every accessor the overridden property has.</summary>
    None,

    /// <summary>The override declares only <c>set</c>; <c>get</c> is the base's.</summary>
    Getter,

    /// <summary>The override declares only <c>get</c>; <c>set</c> (or <c>init</c>) is the base's.</summary>
    Setter,
}
