namespace Metano.Compiler.IR;

/// <summary>
/// Describes the accessor pattern of a property.
/// </summary>
public enum IrPropertyAccessors
{
    /// <summary>Read-only (getter only).</summary>
    GetOnly,

    /// <summary>Read-write (getter + setter).</summary>
    GetSet,

    /// <summary>Read + init-only setter (C# <c>init</c>).</summary>
    GetInit,
}
