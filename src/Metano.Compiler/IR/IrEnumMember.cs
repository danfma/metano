namespace Metano.Compiler.IR;

/// <summary>
/// A member of an enum declaration.
/// </summary>
/// <param name="Name">Member name in original C# casing.</param>
/// <param name="Value">The constant value (numeric or string depending on <see cref="IrEnumStyle"/>).</param>
/// <param name="Attributes">Member-level attributes (e.g., <c>[Name("x")]</c> for name overrides).</param>
public sealed record IrEnumMember(
    string Name,
    object? Value,
    IReadOnlyList<IrAttribute>? Attributes = null
);

/// <summary>
/// How the enum values are represented.
/// </summary>
public enum IrEnumStyle
{
    /// <summary>Standard numeric enum.</summary>
    Numeric,

    /// <summary>String-based enum (C# <c>[StringEnum]</c>).</summary>
    String,
}
