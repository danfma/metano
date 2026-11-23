using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>MS0072</c> errors of the object contracts whose properties share a
/// JSON name (#442). System.Text.Json throws <c>InvalidOperationException</c>
/// "The JSON property name for 'Coll.id' collides with another property." on
/// the contract's first use (measured on .NET 10): two names a naming policy
/// folds together (<c>ID</c> / <c>Id</c> under camelCase, <c>FooBar</c> /
/// <c>Foo_Bar</c> under snake_case), an explicit <c>[JsonPropertyName]</c>
/// equal to another property's name, an inherited property included. Names
/// compare ordinally, ignoring case when the context reads names
/// case-insensitively (<c>PropertyNameCaseInsensitive</c>, Web defaults); a
/// redeclaration under the same JSON name is one property.
/// </summary>
internal sealed class JsonPropertyNameCollisions(
    JsonPropertyCollector properties,
    bool caseInsensitive
)
{
    private readonly JsonPropertyCollector _properties = properties;

    private readonly StringComparer _names = caseInsensitive
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>One error per contract, generic constructions sharing their definition's.</summary>
    public IEnumerable<MetanoDiagnostic> ErrorsOf(IReadOnlyList<JsonContractEntry> entries) =>
        entries
            .Where(entry => entry.Kind == JsonPropertyKind.Ref)
            .Select(entry => entry.Type)
            .OfType<INamedTypeSymbol>()
            .Select(CollisionOf)
            .OfType<MetanoDiagnostic>()
            .DistinctBy(error => (error.Message, error.Location));

    /// <summary>The error at the first property whose JSON name an earlier one already takes.</summary>
    private MetanoDiagnostic? CollisionOf(INamedTypeSymbol contract)
    {
        var taken = new HashSet<string>(_names);

        var colliding = _properties
            .ContractPropertiesOf(contract)
            .DistinctBy(property => (property.Member.Name, property.JsonName))
            .FirstOrDefault(property => !taken.Add(property.JsonName));

        if (colliding is null)
            return null;

        var definition = colliding.Member.OriginalDefinition;
        var contractName = UnmirroredJsonWarning.TypeName(contract.OriginalDefinition);

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.JsonPropertyNameCollision,
            $"The JSON property name for '{contractName}.{colliding.JsonName}' collides with "
                + "another property: System.Text.Json throws InvalidOperationException for "
                + "this contract. Give one of them another name with [JsonPropertyName].",
            UnmirroredJsonWarning.At(definition)
        );
    }
}
