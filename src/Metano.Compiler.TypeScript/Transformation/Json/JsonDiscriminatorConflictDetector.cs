using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Finds the JSON properties that make a tagged payload unfaithful
/// (spec 009 FR-012): a final name equal to the discriminator
/// (System.Text.Json throws <c>InvalidOperationException</c>) or an
/// integer-like name (JavaScript enumerates it before the tag). Only the
/// contracts that are written with a tag count — the tagged registrations,
/// inherited properties included — so untagged registrations, and a
/// hierarchy without any tag, never conflict (measured on .NET 10).
/// </summary>
internal sealed class JsonDiscriminatorConflictDetector(JsonPropertyCollector properties)
{
    private const ulong MaxArrayIndex = 4_294_967_294;

    private readonly JsonPropertyCollector _properties = properties;

    public List<JsonPolymorphismConflict> ConflictsOf(JsonPolymorphism polymorphism) =>
        polymorphism
            .Registrations.Where(registration => registration.IsTagged)
            .Select(registration => registration.Type)
            .OfType<INamedTypeSymbol>()
            .SelectMany(_properties.ContractPropertiesOf)
            .DistinctBy(property => (property.Member, property.JsonName))
            .Select(property => ConflictOf(polymorphism, property))
            .OfType<JsonPolymorphismConflict>()
            .ToList();

    /// <summary>
    /// The conflict of one property, named through the property's definition
    /// so every construction of a generic base reports the same text.
    /// </summary>
    private static JsonPolymorphismConflict? ConflictOf(
        JsonPolymorphism polymorphism,
        JsonSerializableProperty property
    )
    {
        var definition = property.Member.OriginalDefinition;
        var member = $"{Display(definition.ContainingType)}.{definition.Name}";
        var location = definition.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        var owner = Display(polymorphism.BaseType.OriginalDefinition);

        if (property.JsonName == polymorphism.Discriminator)
        {
            return new JsonPolymorphismConflict(
                JsonPolymorphismMessages.DiscriminatorConflict(member, property.JsonName, owner),
                location
            );
        }

        if (IsIntegerLike(property.JsonName))
        {
            return new JsonPolymorphismConflict(
                JsonPolymorphismMessages.IntegerLikeName(property.JsonName, member),
                location
            );
        }

        return null;
    }

    /// <summary>
    /// A canonical array index (<c>0</c>, <c>17</c>, not <c>01</c>), which
    /// JavaScript enumerates before every other key.
    /// </summary>
    private static bool IsIntegerLike(string name) =>
        name.Length > 0
        && name.All(char.IsAsciiDigit)
        && (name == "0" || name[0] != '0')
        && ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
        && index <= MaxArrayIndex;

    private static string Display(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
}
