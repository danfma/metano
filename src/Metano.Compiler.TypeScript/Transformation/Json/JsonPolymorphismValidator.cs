using System.Globalization;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Validates the polymorphism of an object contract the closure reaches
/// against the rules the generated context can honor (spec 009 FR-012,
/// FR-013, FR-015, FR-047), reporting <c>MS0032</c> / <c>MS0033</c>. A
/// constructed generic base is validated per construction, so a registration
/// that does not fit <c>GBase&lt;string&gt;</c> is reported only when the
/// context reaches <c>GBase&lt;string&gt;</c>, as System.Text.Json throws only
/// then. A diagnostic every construction shares is reported once. Hierarchies
/// with an interface base or a <c>[PlainObject]</c> base or variant are left
/// to slice S6c (FR-018, FR-019) and validated as non-polymorphic.
/// </summary>
internal sealed class JsonPolymorphismValidator(
    JsonPropertyCollector properties,
    JsonContractAdmission admission,
    Action<MetanoDiagnostic> report
)
{
    private const ulong MaxArrayIndex = 4_294_967_294;

    private readonly JsonPropertyCollector _properties = properties;
    private readonly JsonContractAdmission _admission = admission;
    private readonly Action<MetanoDiagnostic> _report = report;
    private readonly HashSet<MetanoDiagnostic> _reported = [];

    /// <summary>
    /// The polymorphism the context emits for <paramref name="baseType"/>:
    /// its valid registrations in attribute order, or <c>null</c> when the
    /// type is not polymorphic, belongs to a structural hierarchy (S6c), or
    /// has a hierarchy-wide <c>MS0032</c> (a property named like the
    /// discriminator or integer-like), which leaves no faithful descriptor.
    /// </summary>
    public JsonPolymorphism? Validate(INamedTypeSymbol baseType)
    {
        if (JsonPolymorphismReader.TryRead(baseType) is not { } declared)
            return null;

        if (IsStructuralHierarchy(declared))
            return null;

        var polymorphism = declared with { Derived = ValidRegistrationsOf(declared) };

        ReportLossyRegistrations(polymorphism);

        var conflicts = WireNameConflictsOf(polymorphism);

        foreach (var (message, location) in conflicts)
            ReportInvalid(message, location);

        return conflicts.Count == 0 ? polymorphism : null;
    }

    /// <summary>
    /// A hierarchy that needs interface-base (FR-018) or <c>[PlainObject]</c>
    /// (FR-019) dispatch, which slice S6c adds. An interface registered on a
    /// class base is not assignable to it and is reported as such.
    /// </summary>
    private static bool IsStructuralHierarchy(JsonPolymorphism polymorphism) =>
        polymorphism.BaseType.TypeKind == TypeKind.Interface
        || polymorphism
            .Derived.Select(registration => registration.Type)
            .Prepend(polymorphism.BaseType)
            .Any(SymbolHelper.HasPlainObject);

    /// <summary>
    /// The registrations the descriptor keeps. Each rejected one is reported;
    /// a repeated type or tag keeps its first registration.
    /// </summary>
    private List<JsonDerivedType> ValidRegistrationsOf(JsonPolymorphism polymorphism)
    {
        var valid = new List<JsonDerivedType>();

        foreach (var registration in polymorphism.Derived)
        {
            var rejection =
                RejectionOf(polymorphism, registration)
                ?? DuplicateRejectionOf(polymorphism, registration, valid);

            if (rejection is not null)
            {
                ReportInvalid(rejection, registration.Location);
                continue;
            }

            valid.Add(registration);
        }

        return valid;
    }

    /// <summary>Why a single registration cannot be honored, or <c>null</c>.</summary>
    private string? RejectionOf(JsonPolymorphism polymorphism, JsonDerivedType registration)
    {
        var registered = Display(registration.Type);
        var owner = Display(polymorphism.BaseType.OriginalDefinition);

        if (registration.Type is INamedTypeSymbol { IsGenericType: true })
            return $"Generic derived type '{registered}' is not supported.";

        if (!IsAssignable(registration.Type, polymorphism.BaseType))
            return NotAssignableMessage(polymorphism, registered, owner);

        if (!_admission.HasObjectContract(registration.Type))
            return $"'{registered}' registered by [JsonDerivedType] on '{owner}' is not "
                + "transpiled, so it has no TypeScript contract.";

        var requiresNearestAncestor =
            registration.IsAbstract
            && polymorphism.UnknownDerivedType
                != JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor;

        if (requiresNearestAncestor)
            return $"'{registered}' is abstract; abstract derived types require "
                + $"UnknownDerivedTypeHandling = FallBackToNearestAncestor on '{owner}' (as in "
                + "System.Text.Json).";

        return null;
    }

    /// <summary>
    /// Why a registration repeats the type or the tag of an earlier
    /// <paramref name="kept"/> one, or <c>null</c>. A string tag never equals
    /// an int tag.
    /// </summary>
    private static string? DuplicateRejectionOf(
        JsonPolymorphism polymorphism,
        JsonDerivedType registration,
        IReadOnlyList<JsonDerivedType> kept
    )
    {
        var owner = Display(polymorphism.BaseType.OriginalDefinition);

        var repeatsType = kept.Any(earlier =>
            SymbolEqualityComparer.Default.Equals(earlier.Type, registration.Type)
        );

        if (repeatsType)
            return $"'{Display(registration.Type)}' is registered twice on '{owner}'.";

        var repeatsTag =
            registration.Tag is not null && kept.Any(earlier => earlier.Tag == registration.Tag);

        if (repeatsTag)
            return $"Type discriminator '{registration.Tag}' is registered twice on '{owner}'.";

        return null;
    }

    /// <summary>
    /// The FR-012 message, or the FR-015 one for a constructed generic base,
    /// which names the construction the context reaches.
    /// </summary>
    private static string NotAssignableMessage(
        JsonPolymorphism polymorphism,
        string registered,
        string owner
    )
    {
        var baseType = polymorphism.BaseType;

        if (baseType.IsGenericType)
            return $"'{registered}' registered on '{owner}' is not assignable to "
                + $"'{Display(baseType)}', which this context reaches.";

        return $"'{registered}' registered by [JsonDerivedType] on '{owner}' is not assignable "
            + $"to '{owner}'.";
    }

    /// <summary>The type itself or one of its base classes is
    /// <paramref name="baseType"/> (the base registering itself is valid).</summary>
    private static bool IsAssignable(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// <c>MS0033</c> for an untagged registration and for a derived type that
    /// is itself polymorphic (FR-013): both behave as in System.Text.Json.
    /// </summary>
    private void ReportLossyRegistrations(JsonPolymorphism polymorphism)
    {
        var owner = Display(polymorphism.BaseType.OriginalDefinition);

        foreach (var registration in polymorphism.Derived)
        {
            var registered = Display(registration.Type);

            if (registration.Tag is null)
                ReportLossy(
                    $"'{registered}' is registered on '{owner}' without a type discriminator; it "
                        + $"serializes without a tag and deserializes as '{owner}' (as in "
                        + "System.Text.Json).",
                    registration.Location
                );

            if (IsNestedPolymorphicBase(polymorphism, registration))
                ReportLossy(
                    $"'{registered}' declares its own [JsonDerivedType]s; as in System.Text.Json, "
                        + $"they apply only through '{registered}' and are not re-dispatched "
                        + $"through '{owner}'.",
                    registration.Location
                );
        }
    }

    private static bool IsNestedPolymorphicBase(
        JsonPolymorphism polymorphism,
        JsonDerivedType registration
    ) =>
        registration.Type is INamedTypeSymbol registered
        && !SymbolEqualityComparer.Default.Equals(registered, polymorphism.BaseType)
        && JsonPolymorphismReader.IsPolymorphic(registered);

    /// <summary>
    /// The <c>MS0032</c> of every JSON property of the hierarchy's contracts
    /// (the base and each valid registration, inherited properties included)
    /// whose final name equals the discriminator (System.Text.Json throws
    /// <c>InvalidOperationException</c>) or is integer-like (JavaScript
    /// enumerates such keys before the tag).
    /// </summary>
    private List<(string Message, Location? Location)> WireNameConflictsOf(
        JsonPolymorphism polymorphism
    ) =>
        polymorphism
            .Derived.Select(registration => (INamedTypeSymbol)registration.Type)
            .Prepend(polymorphism.BaseType)
            .SelectMany(_properties.ContractPropertiesOf)
            .DistinctBy(property => (property.Property, property.JsonName))
            .Select(property => WireNameConflictOf(polymorphism, property))
            .OfType<(string Message, Location? Location)>()
            .ToList();

    private static (string Message, Location? Location)? WireNameConflictOf(
        JsonPolymorphism polymorphism,
        JsonSerializableProperty property
    )
    {
        var member = $"{Display(property.Property.ContainingType)}.{property.Property.Name}";
        var location = property.Property.Locations.FirstOrDefault(l => l.IsInSource);

        if (property.JsonName == polymorphism.Discriminator)
            return (
                $"'{member}' serializes as '{property.JsonName}', which conflicts with the "
                    + $"discriminator of '{Display(polymorphism.BaseType.OriginalDefinition)}'.",
                location
            );

        if (IsIntegerLike(property.JsonName))
            return (
                $"JSON property '{property.JsonName}' of '{member}' is integer-like; JavaScript "
                    + "would order it before the discriminator.",
                location
            );

        return null;
    }

    /// <summary>A canonical array index (<c>0</c>, <c>17</c>, not <c>01</c>),
    /// which JavaScript enumerates before every other key.</summary>
    private static bool IsIntegerLike(string name) =>
        name.Length > 0
        && name.All(char.IsAsciiDigit)
        && (name == "0" || name[0] != '0')
        && ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
        && index <= MaxArrayIndex;

    private void ReportInvalid(string message, Location? location) =>
        Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.InvalidJsonPolymorphism,
                message,
                location
            )
        );

    private void ReportLossy(string message, Location? location) =>
        Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.LossyJsonPolymorphism,
                message,
                location
            )
        );

    private void Report(MetanoDiagnostic diagnostic)
    {
        if (_reported.Add(diagnostic))
            _report(diagnostic);
    }

    private static string Display(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
}
