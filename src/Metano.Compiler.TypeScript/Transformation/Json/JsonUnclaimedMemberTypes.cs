using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>MS0001</c> errors of the serialized members whose descriptor points
/// at a type the context cannot claim a spec for (#713): <c>object</c>, a
/// BCL struct or class such as <c>KeyValuePair&lt;K, V&gt;</c>,
/// <c>ValueTuple</c> or <c>Version</c>, an <c>[Ignore]</c> / <c>[External]</c>
/// type, or a type of a non-Metano assembly. Such a member used to emit a
/// <c>ref</c> to a getter the context never declares: tsgo rejected the
/// context and the runtime crashed on the first read or write. A
/// <c>[JsonSerializable]</c> root of the same types is <c>MS0035</c>.
/// </summary>
internal sealed class JsonUnclaimedMemberTypes(
    JsonPropertyCollector properties,
    JsonContractAdmission admission
)
{
    private readonly JsonPropertyCollector _properties = properties;
    private readonly JsonContractAdmission _admission = admission;

    /// <summary>One error per member, generic constructions sharing their definition's.</summary>
    public IEnumerable<MetanoDiagnostic> ErrorsOf(IReadOnlyList<JsonContractEntry> entries) =>
        entries
            .Where(entry => entry.Kind == JsonPropertyKind.Ref)
            .Select(entry => entry.Type)
            .OfType<INamedTypeSymbol>()
            .SelectMany(_properties.DeclarationOrderPropertiesOf)
            .Select(ErrorOf)
            .OfType<MetanoDiagnostic>()
            .DistinctBy(error => (error.Message, error.Location));

    private MetanoDiagnostic? ErrorOf(JsonSerializableProperty property)
    {
        var unclaimed = JsonContractClosure
            .RefTargetsWithin(property.Type)
            .FirstOrDefault(target => !_admission.HasRefSpec(target));

        if (unclaimed is null)
            return null;

        var member = property.Member.OriginalDefinition;

        return new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnsupportedFeature,
            $"'{UnmirroredJsonWarning.MemberName(member)}' cannot be serialized by the "
                + $"TypeScript JSON context: '{UnmirroredJsonWarning.TypeName(unclaimed)}' has "
                + "no JSON contract in it (it is not a transpiled class, struct or interface of "
                + "this project or of a referenced Metano package). Change the member's type, "
                + "or leave it out with [JsonIgnore].",
            UnmirroredJsonWarning.At(member)
        );
    }
}
