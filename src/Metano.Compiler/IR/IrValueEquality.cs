namespace Metano.Compiler.IR;

/// <summary>
/// A value-equality member C# synthesizes or inherits on a record or a plain
/// struct (baseline FR-060), which a target that supplies its name inserts into
/// the overload group spelled like it (spec 013 FR-038).
/// </summary>
public enum IrSynthesizedValueMember
{
    /// <summary><c>Equals(object)</c>: a record's synthesized override, a struct's <c>ValueType.Equals</c>.</summary>
    Equals,

    /// <summary><c>GetHashCode()</c>.</summary>
    HashCode,

    /// <summary>The copy behind a record's <c>with</c> expression.</summary>
    Copy,
}

/// <summary>Which value equality C# gives a type.</summary>
public enum IrValueEqualityKind
{
    /// <summary>None a target synthesizes: a class, an interface, a <c>[PlainObject]</c>,
    /// <c>[JsTuple]</c>, <c>[Branded]</c> or JSX component type.</summary>
    None,

    /// <summary>A record emitted as a class: <c>Equals</c>, <c>GetHashCode</c> and the copy.</summary>
    Record,

    /// <summary>A plain struct emitted as a class: <c>ValueType.Equals</c> and <c>GetHashCode</c>.</summary>
    Struct,
}

/// <summary>
/// The value equality of a type, read from its symbols (spec 013 FR-045): the
/// single source every target and the runtime-requirement scanner read.
/// </summary>
/// <param name="Kind">Which members C# synthesizes.</param>
/// <param name="ReplacesEquals">The type declares the member C# uses instead of
/// the synthesized <c>Equals(object)</c>: a record's <c>Equals(R)</c>, a struct's
/// <c>override Equals(object)</c>. A struct's <c>Equals(S)</c> replaces nothing,
/// since <c>ValueType.Equals</c> never calls it.</param>
/// <param name="ReplacesHashCode">The type declares <c>override GetHashCode()</c>.</param>
public sealed record IrValueEqualityShape(
    IrValueEqualityKind Kind,
    bool ReplacesEquals = false,
    bool ReplacesHashCode = false
)
{
    /// <summary>The shape of a type with no synthesized value equality.</summary>
    public static IrValueEqualityShape None { get; } = new(IrValueEqualityKind.None);

    /// <summary>Whether C# synthesizes <paramref name="member"/> on the type.</summary>
    public bool Synthesizes(IrSynthesizedValueMember member) =>
        member switch
        {
            IrSynthesizedValueMember.Copy => Kind is IrValueEqualityKind.Record,
            _ => Kind is not IrValueEqualityKind.None,
        };
}
