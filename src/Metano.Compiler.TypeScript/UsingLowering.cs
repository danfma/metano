namespace Metano.Compiler.TypeScript;

/// <summary>
/// How the TypeScript target emits a C# <c>using</c> statement or declaration (FR-111).
/// </summary>
public enum UsingLowering
{
    /// <summary>
    /// A TypeScript <c>using</c> declaration (the default). Consumers need
    /// <c>ESNext.Disposable</c> in their <c>lib</c>; below target <c>ESNext</c>, TypeScript
    /// downlevels it to its disposal helpers.
    /// </summary>
    Native,

    /// <summary>
    /// The <c>try</c>/<c>finally</c> lowering of 3.3.0, which calls <c>dispose()</c> on each
    /// resource in reverse order.
    /// </summary>
    TryFinally,
}
