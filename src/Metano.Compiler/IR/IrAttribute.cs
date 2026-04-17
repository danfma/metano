namespace Metano.Compiler.IR;

/// <summary>
/// A semantic annotation carried on an IR type or member.
/// Captures Metano-specific attributes (e.g., <c>[GenerateGuard]</c>,
/// <c>[StringEnum]</c>) that influence how a target backend renders the declaration.
/// </summary>
/// <param name="Name">Attribute name without the <c>Attribute</c> suffix.</param>
/// <param name="Arguments">Positional/named argument values, if any.</param>
public sealed record IrAttribute(
    string Name,
    IReadOnlyDictionary<string, object?>? Arguments = null
);
