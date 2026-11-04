using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c> description of a
/// polymorphic base (spec 009 FR-001): the discriminator name, written
/// verbatim, the unknown-type handling, the ignore flag, and the derived
/// registrations in attribute order. <see cref="BaseType"/> is the base the
/// context reaches, a constructed type for a closed generic base (FR-015).
/// </summary>
internal sealed record JsonPolymorphism(
    INamedTypeSymbol BaseType,
    string Discriminator,
    JsonUnknownDerivedTypeHandling UnknownDerivedType,
    bool IgnoreUnrecognizedTags,
    IReadOnlyList<JsonDerivedType> Derived
)
{
    /// <summary>The discriminator System.Text.Json uses when none is set.</summary>
    public const string DefaultDiscriminator = "$type";

    public bool IsAbstractBase => BaseType.IsAbstract;
}

/// <summary>
/// One <c>[JsonDerivedType]</c> registration: the registered type (possibly
/// the base itself), its tag (<c>null</c> for an untagged registration) and
/// the attribute's location.
/// </summary>
internal sealed record JsonDerivedType(ITypeSymbol Type, JsonTypeTag? Tag, Location? Location)
{
    public bool IsAbstract => Type.IsAbstract;
}

/// <summary>
/// A <c>[JsonDerivedType]</c> type discriminator: a JSON string or a JSON
/// number. The two are distinct values (<c>1</c> is not <c>"1"</c>).
/// </summary>
internal abstract record JsonTypeTag
{
    private JsonTypeTag() { }

    public sealed record Text(string Value) : JsonTypeTag
    {
        public override string ToString() => Value;
    }

    public sealed record Number(int Value) : JsonTypeTag
    {
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// <c>JsonUnknownDerivedTypeHandling</c>, with the values of the
/// System.Text.Json enum.
/// </summary>
internal enum JsonUnknownDerivedTypeHandling
{
    FailSerialization = 0,
    FallBackToBaseType = 1,
    FallBackToNearestAncestor = 2,
}
