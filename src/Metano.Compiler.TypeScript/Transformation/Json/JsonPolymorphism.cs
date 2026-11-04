using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c> description of a
/// polymorphic base (spec 009 FR-001): the discriminator name, written
/// verbatim, the unknown-type handling, the ignore flag, and the
/// registrations in attribute order. <see cref="BaseType"/> is the base the
/// context reaches, a constructed type for a closed generic base (FR-015);
/// <see cref="Location"/> is its <c>[JsonPolymorphic]</c> attribute, else its
/// first <c>[JsonDerivedType]</c>.
/// </summary>
internal sealed record JsonPolymorphism(
    INamedTypeSymbol BaseType,
    string Discriminator,
    JsonUnknownDerivedTypeHandling UnknownDerivedType,
    bool IgnoreUnrecognizedTags,
    IReadOnlyList<JsonDerivedTypeRegistration> Registrations,
    Location? Location
)
{
    /// <summary>The discriminator System.Text.Json uses when none is set.</summary>
    public const string DefaultDiscriminator = "$type";

    /// <summary>
    /// The registrations' guard test order (FR-019 rule 3): indices into
    /// <see cref="Registrations"/>, most specific first; <c>null</c> when it
    /// is attribute order.
    /// </summary>
    public IReadOnlyList<int>? TestOrder { get; init; }

    /// <summary>An abstract class or an interface (both are <c>IsAbstract</c>).</summary>
    public bool IsAbstractBase => BaseType.IsAbstract;

    /// <summary>
    /// The base has no TypeScript constructor (an interface, FR-018, or a
    /// <c>[PlainObject]</c> type, FR-019), so the descriptor names it with
    /// <c>typeName</c>.
    /// </summary>
    public bool HasNoConstructor => !JsonContractAdmission.HasRuntimeConstructor(BaseType);

    /// <summary>The base registers itself as one of its derived types.</summary>
    public bool RegistersItself =>
        Registrations.Any(registration =>
            SymbolEqualityComparer.Default.Equals(registration.Type, BaseType)
        );

    /// <summary>
    /// A concrete <c>[PlainObject]</c> base that does not register itself: a
    /// plain object no variant guard accepts is written as the base when the
    /// base's own guard accepts it (FR-019 rule 5, the <c>baseTest</c>).
    /// </summary>
    public bool NeedsBaseTest =>
        JsonStructuralGuardTargets.IsConcretePlainObjectBase(BaseType) && !RegistersItself;

    /// <summary>
    /// Whether any registration declares a tag. Without one, System.Text.Json
    /// reads no metadata at all: the base reads like a non-polymorphic type.
    /// </summary>
    public bool HasTaggedRegistration => Registrations.Any(registration => registration.IsTagged);
}

/// <summary>
/// One <c>[JsonDerivedType]</c> registration: the registered type (possibly
/// the base itself), its tag (<c>null</c> for an untagged registration) and
/// the attribute's location.
/// </summary>
internal sealed record JsonDerivedTypeRegistration(
    ITypeSymbol Type,
    JsonTypeTag? Tag,
    Location? Location
)
{
    public bool IsAbstract => Type.IsAbstract;

    public bool IsTagged => Tag is not null;

    /// <summary>
    /// A <c>[PlainObject]</c> registration, selected on write by its
    /// synthesized guard (FR-019).
    /// </summary>
    public bool IsStructural => SymbolHelper.HasPlainObject(Type);
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
