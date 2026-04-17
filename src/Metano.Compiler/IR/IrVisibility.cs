namespace Metano.Compiler.IR;

/// <summary>
/// Access level of a type or member in the IR.
/// Maps from C# accessibility; each target backend decides how to render it
/// (e.g., TypeScript has no <c>protected internal</c>).
/// </summary>
public enum IrVisibility
{
    Public,
    Protected,
    Internal,

    /// <summary>C# <c>protected internal</c> (<c>ProtectedOrInternal</c> in Roslyn).</summary>
    ProtectedInternal,

    /// <summary>C# <c>private protected</c> (<c>ProtectedAndInternal</c> in Roslyn).</summary>
    PrivateProtected,

    Private,
}
