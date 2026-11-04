using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// Validates the polymorphism of an object contract the context dispatches
/// through against the rules the generated context can honor (spec 009
/// FR-012, FR-013, FR-015, FR-047), reporting <c>MS0032</c> / <c>MS0033</c>.
/// A constructed generic base is validated per construction, so a
/// registration that does not fit <c>GBase&lt;string&gt;</c> is reported only
/// when the context reaches <c>GBase&lt;string&gt;</c>, as System.Text.Json
/// throws only then. A diagnostic every construction shares is reported once.
/// Interface bases (FR-018) follow the class rules; <c>[PlainObject]</c>
/// variants are ordered by <see cref="JsonStructuralVariants"/>, which also
/// rejects variants that cannot be told apart on write (FR-019).
/// </summary>
internal sealed class JsonPolymorphismValidator(
    JsonPropertyCollector properties,
    JsonContractAdmission admission,
    JsonStructuralVariants structuralVariants,
    TypeGuardBuilder guards,
    Action<MetanoDiagnostic> report
)
{
    private static readonly HashSet<string> ReservedDiscriminators = new(StringComparer.Ordinal)
    {
        "$id",
        "$ref",
        "$values",
    };

    private readonly JsonContractAdmission _admission = admission;
    private readonly JsonDiscriminatorConflictDetector _conflicts = new(properties);
    private readonly JsonStructuralVariants _structuralVariants = structuralVariants;
    private readonly TypeGuardBuilder _guards = guards;
    private readonly Action<MetanoDiagnostic> _report = report;
    private readonly HashSet<MetanoDiagnostic> _reported = [];

    /// <summary>
    /// The polymorphism the context emits for <paramref name="baseType"/>:
    /// its valid registrations in attribute order with their guard test
    /// order, or <c>null</c> when the type is not polymorphic or has an error
    /// no descriptor can carry (a sealed base, no registration, a reserved
    /// discriminator, a property that conflicts with the tag,
    /// <c>[PlainObject]</c> variants that cannot be told apart). Reports the
    /// diagnostics as a side effect.
    /// </summary>
    public JsonPolymorphism? ValidatedPolymorphismOf(INamedTypeSymbol baseType)
    {
        if (JsonPolymorphismReader.TryRead(baseType) is not { } declared)
            return null;

        if (BaseRejectionOf(declared) is { } rejection)
        {
            ReportInvalid(rejection, declared.Location);
            return null;
        }

        var polymorphism = declared with { Registrations = ValidRegistrationsOf(declared) };

        ReportLossyRegistrations(polymorphism);

        var structuralDispatch = _structuralVariants.DispatchOf(polymorphism);

        var conflicts = _conflicts
            .ConflictsOf(polymorphism)
            .Concat(structuralDispatch.Conflicts)
            .Concat(GuardlessBaseConflictsOf(polymorphism))
            .ToList();

        foreach (var conflict in conflicts)
            ReportInvalid(conflict.Message, conflict.Location);

        return conflicts.Count == 0
            ? polymorphism with
            {
                TestOrder = structuralDispatch.TestOrder,
            }
            : null;
    }

    /// <summary>
    /// Why System.Text.Json rejects the base itself (it throws
    /// <c>InvalidOperationException</c>), or <c>null</c>. A struct cannot
    /// carry the attributes at all (CS0592).
    /// </summary>
    private static string? BaseRejectionOf(JsonPolymorphism polymorphism)
    {
        var owner = OwnerOf(polymorphism);

        if (polymorphism.BaseType.IsSealed)
            return JsonPolymorphismMessages.SealedBase(owner);

        if (polymorphism.Registrations.Count == 0)
            return JsonPolymorphismMessages.NoRegistration(owner);

        if (ReservedDiscriminators.Contains(polymorphism.Discriminator))
            return JsonPolymorphismMessages.ReservedDiscriminator(
                polymorphism.Discriminator,
                owner
            );

        return null;
    }

    /// <summary>
    /// The registrations the descriptor keeps. Each rejected one is reported;
    /// a repeated type or tag keeps its first registration.
    /// </summary>
    private List<JsonDerivedTypeRegistration> ValidRegistrationsOf(JsonPolymorphism polymorphism)
    {
        var valid = new List<JsonDerivedTypeRegistration>();

        foreach (var registration in polymorphism.Registrations)
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
    private string? RejectionOf(
        JsonPolymorphism polymorphism,
        JsonDerivedTypeRegistration registration
    )
    {
        var registered = Display(registration.Type);
        var owner = OwnerOf(polymorphism);

        if (registration.Type is INamedTypeSymbol { IsGenericType: true })
            return JsonPolymorphismMessages.GenericRegistration(registered);

        if (!IsAssignable(registration.Type, polymorphism.BaseType))
            return NotAssignableMessage(polymorphism, registered, owner);

        // OPEN-18: a TS value carries no interface identity, and a guard for
        // an interface entry would overlap its implementations'.
        if (registration.Type.TypeKind == TypeKind.Interface)
            return JsonPolymorphismMessages.InterfaceRegistration(registered, owner);

        if (!_admission.HasObjectContract(registration.Type))
            return JsonPolymorphismMessages.NotTranspiled(registered, owner);

        if (registration.IsStructural && !_guards.CanGenerateGuard(registration.NamedType))
            return JsonPolymorphismMessages.GuardlessRegistration(registered, owner);

        var requiresNearestAncestor =
            registration.IsAbstract
            && polymorphism.UnknownDerivedType
                != JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor;

        if (requiresNearestAncestor)
            return JsonPolymorphismMessages.AbstractWithoutNearestAncestor(registered, owner);

        return null;
    }

    /// <summary>
    /// Why a registration repeats the type or the tag of an earlier
    /// <paramref name="kept"/> one, or <c>null</c>. A string tag never equals
    /// an int tag.
    /// </summary>
    private static string? DuplicateRejectionOf(
        JsonPolymorphism polymorphism,
        JsonDerivedTypeRegistration registration,
        IReadOnlyList<JsonDerivedTypeRegistration> kept
    )
    {
        var owner = OwnerOf(polymorphism);

        var repeatsType = kept.Any(earlier =>
            SymbolEqualityComparer.Default.Equals(earlier.Type, registration.Type)
        );

        if (repeatsType)
            return JsonPolymorphismMessages.RepeatedType(Display(registration.Type), owner);

        if (registration.Tag is { } tag && kept.Any(earlier => earlier.Tag == tag))
            return JsonPolymorphismMessages.RepeatedTag(tag.ToString(), owner);

        return null;
    }

    /// <summary>
    /// A concrete <c>[PlainObject]</c> base whose <c>baseTest</c> would
    /// reference a guard that is never emitted (FR-019 rule 5).
    /// </summary>
    private IEnumerable<JsonPolymorphismConflict> GuardlessBaseConflictsOf(
        JsonPolymorphism polymorphism
    )
    {
        if (!polymorphism.NeedsBaseTest || _guards.CanGenerateGuard(polymorphism.BaseType))
            return [];

        return
        [
            new JsonPolymorphismConflict(
                JsonPolymorphismMessages.GuardlessBase(OwnerOf(polymorphism)),
                polymorphism.Location
            ),
        ];
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
        {
            return JsonPolymorphismMessages.NotAssignableToConstruction(
                registered,
                owner,
                Display(baseType)
            );
        }

        return JsonPolymorphismMessages.NotAssignable(registered, owner);
    }

    /// <summary>
    /// The type itself, one of its base classes or, for an interface base
    /// (FR-018), one of its interfaces is <paramref name="baseType"/> (the
    /// base registering itself is valid).
    /// </summary>
    private static bool IsAssignable(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        if (baseType.TypeKind == TypeKind.Interface && ImplementsInterface(type, baseType))
            return true;

        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }

    private static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol interfaceType) =>
        type.AllInterfaces.Any(implemented =>
            SymbolEqualityComparer.Default.Equals(implemented, interfaceType)
        );

    /// <summary>
    /// <c>MS0033</c> for an untagged registration and for a derived type that
    /// is itself polymorphic (FR-013): both behave as in System.Text.Json.
    /// </summary>
    private void ReportLossyRegistrations(JsonPolymorphism polymorphism)
    {
        var owner = OwnerOf(polymorphism);

        foreach (var registration in polymorphism.Registrations)
        {
            var registered = Display(registration.Type);

            if (!registration.IsTagged)
            {
                ReportLossy(
                    JsonPolymorphismMessages.UntaggedRegistration(
                        registered,
                        owner,
                        polymorphism.IsAbstractBase
                    ),
                    registration.Location
                );
            }

            if (IsNestedPolymorphicBase(polymorphism, registration))
            {
                ReportLossy(
                    JsonPolymorphismMessages.NestedPolymorphicRegistration(registered, owner),
                    registration.Location
                );
            }
        }
    }

    private static bool IsNestedPolymorphicBase(
        JsonPolymorphism polymorphism,
        JsonDerivedTypeRegistration registration
    ) =>
        registration.Type is INamedTypeSymbol registered
        && !SymbolEqualityComparer.Default.Equals(registered, polymorphism.BaseType)
        && JsonPolymorphismReader.IsPolymorphic(registered);

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

    /// <summary>
    /// The base as messages name it: its definition, so every construction of
    /// a generic base reports the same text (<c>GBase&lt;T&gt;</c>).
    /// </summary>
    private static string OwnerOf(JsonPolymorphism polymorphism) =>
        Display(polymorphism.BaseType.OriginalDefinition);

    private static string Display(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
}
