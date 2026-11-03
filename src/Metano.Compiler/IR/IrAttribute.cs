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
)
{
    /// <summary>The attribute name of a <c>[Name]</c> override.</summary>
    public const string NameOverrideAttribute = "Name";

    /// <summary>The argument key of the untargeted <c>[Name("…")]</c> value.</summary>
    public const string NameOverrideValueArgument = "Value";

    /// <summary>An untargeted <c>[Name]</c> override carrying <paramref name="name"/>.</summary>
    public static IrAttribute NameOverride(string name) =>
        new(
            NameOverrideAttribute,
            new Dictionary<string, object?> { [NameOverrideValueArgument] = name }
        );
}
