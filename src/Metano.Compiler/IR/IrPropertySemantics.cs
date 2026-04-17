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
public sealed record IrPropertySemantics(
    bool HasGetterBody = false,
    bool HasSetterBody = false,
    bool HasInitializer = false,
    bool IsAbstract = false,
    bool IsVirtual = false,
    bool IsOverride = false,
    bool IsSealed = false
);
