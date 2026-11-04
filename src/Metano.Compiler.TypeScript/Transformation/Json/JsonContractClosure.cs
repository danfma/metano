using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// One member of the generated context: an object contract
/// (<see cref="JsonPropertyKind.Ref"/>, emitted as a <c>TypeSpec</c>) or a
/// value root (every other kind, emitted as a <c>ValueSpec</c>).
/// <see cref="Root"/> is the first <c>[JsonSerializable]</c> application that
/// names the type, or <c>null</c> for a type the closure discovered.
/// </summary>
internal sealed record JsonContractEntry(
    ITypeSymbol Type,
    JsonPropertyKind Kind,
    JsonContractRoot? Root
);

/// <summary>
/// The contract closure of a context: its entries in emission order; for
/// every entry some <c>ref</c> descriptor, <c>base</c> link or derived link
/// points at, the first entry that points at it; and the validated
/// polymorphism of every polymorphic object contract.
/// </summary>
internal sealed record JsonContract(
    IReadOnlyList<JsonContractEntry> Entries,
    IReadOnlyDictionary<ITypeSymbol, ITypeSymbol> Referrers,
    IReadOnlyDictionary<ITypeSymbol, JsonPolymorphism> Polymorphisms
);

/// <summary>
/// Computes the contract closure of the <c>[JsonSerializable]</c> roots: the
/// roots in declaration order, followed by every object type reached through
/// their serializable properties (walking the same nullable / array / map /
/// hash-set shapes the descriptors emit), through their <c>base</c> link, or
/// through the descriptor of a value root (its element, map value or nullable
/// inner), or through a <c>[JsonDerivedType]</c> of a polymorphic base
/// (FR-003, attribute order), in breadth-first discovery order. Like
/// System.Text.Json, the context therefore carries a spec for every
/// <c>ref</c> descriptor, <c>base</c> link and derived link it emits.
/// Entries are deduplicated by type symbol, so cycles (<c>A → B → A</c>)
/// terminate and each type is emitted once; name collisions between distinct
/// types are resolved afterwards by <see cref="JsonTypeInfoNames"/>.
/// </summary>
internal sealed class JsonContractClosure(
    JsonPropertyCollector properties,
    JsonContractAdmission admission,
    JsonPolymorphismValidator polymorphismValidator
)
{
    private readonly JsonPropertyCollector _properties = properties;
    private readonly JsonContractAdmission _admission = admission;
    private readonly JsonPolymorphismValidator _polymorphismValidator = polymorphismValidator;

    /// <summary>
    /// Walks the closure breadth-first. Polymorphism is read only for the
    /// <em>dispatch targets</em> — roots, <c>ref</c> targets and registered
    /// types — never for a type reached only through a <c>base</c> link:
    /// System.Text.Json never builds the polymorphism of such a base, so
    /// neither its registrations nor its diagnostics apply (DECIDED-22 j). A
    /// type that becomes a dispatch target after it was walked is expanded
    /// then.
    /// </summary>
    public JsonContract Collect(IReadOnlyList<JsonContractRoot> roots)
    {
        var entries = new Dictionary<ITypeSymbol, JsonContractEntry>(
            SymbolEqualityComparer.Default
        );
        var ordered = new List<JsonContractEntry>();
        var referrers = new Dictionary<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var polymorphisms = new Dictionary<ITypeSymbol, JsonPolymorphism>(
            SymbolEqualityComparer.Default
        );
        var dispatchTargets = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var walked = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var expanded = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<JsonContractEntry>();

        foreach (var root in roots)
        {
            Enqueue(new JsonContractEntry(root.Type, root.Kind, root));
            MarkDispatchTarget(root.Type);
        }

        while (pending.TryDequeue(out var current))
        {
            walked.Add(current.Type);

            if (LinkedBaseOf(current) is { } baseType)
                Link(current, baseType, isDispatchTarget: false);

            foreach (var target in RefTargetsOf(current).Where(_admission.HasObjectContract))
                Link(current, target, isDispatchTarget: true);

            if (dispatchTargets.Contains(current.Type))
                ExpandPolymorphism(current);
        }

        return new JsonContract(ordered, referrers, polymorphisms);

        void Link(JsonContractEntry referrer, ITypeSymbol target, bool isDispatchTarget)
        {
            referrers.TryAdd(target, referrer.Type);
            Enqueue(new JsonContractEntry(target, JsonPropertyKind.Ref, Root: null));

            if (isDispatchTarget)
                MarkDispatchTarget(target);
        }

        void Enqueue(JsonContractEntry entry)
        {
            if (!entries.TryAdd(entry.Type, entry))
                return;

            ordered.Add(entry);
            pending.Enqueue(entry);
        }

        void MarkDispatchTarget(ITypeSymbol type)
        {
            if (dispatchTargets.Add(type) && walked.Contains(type))
                ExpandPolymorphism(entries[type]);
        }

        // A type can become a dispatch target while it is being walked (a
        // self-referencing base reached first through a `base` link), so the
        // walk and MarkDispatchTarget may both ask for the same expansion.
        void ExpandPolymorphism(JsonContractEntry entry)
        {
            if (!expanded.Add(entry.Type))
                return;

            if (ValidatedPolymorphismOf(entry) is not { } polymorphism)
                return;

            polymorphisms.Add(entry.Type, polymorphism);

            foreach (var registeredType in RegisteredTypesOf(polymorphism))
                Link(entry, registeredType, isDispatchTarget: true);
        }
    }

    /// <summary>
    /// The base type an object contract links to through <c>base</c>, which
    /// the context always carries (<see cref="JsonPropertyCollector.LinkedBaseOf"/>).
    /// </summary>
    private INamedTypeSymbol? LinkedBaseOf(JsonContractEntry entry) =>
        entry.Kind == JsonPropertyKind.Ref
            ? _properties.LinkedBaseOf((INamedTypeSymbol)entry.Type)
            : null;

    /// <summary>
    /// The validated polymorphism of an object contract, whose registered
    /// types the closure adds (FR-003); <c>null</c> for a value root and a
    /// non-polymorphic contract. Validating reports the diagnostics.
    /// </summary>
    private JsonPolymorphism? ValidatedPolymorphismOf(JsonContractEntry entry) =>
        entry.Kind == JsonPropertyKind.Ref
            ? _polymorphismValidator.ValidatedPolymorphismOf((INamedTypeSymbol)entry.Type)
            : null;

    /// <summary>
    /// The registered types a polymorphic base links to, leaving out a
    /// self-registration (the base is already an entry).
    /// </summary>
    private static IEnumerable<ITypeSymbol> RegisteredTypesOf(JsonPolymorphism polymorphism) =>
        polymorphism
            .Registrations.Select(registration => registration.Type)
            .Where(type => !SymbolEqualityComparer.Default.Equals(type, polymorphism.BaseType));

    /// <summary>
    /// The object types an entry's <c>ref</c> descriptors point at: for an
    /// object contract, the ones its properties reach; for a value root, the
    /// ones its own descriptor reaches.
    /// </summary>
    private IEnumerable<ITypeSymbol> RefTargetsOf(JsonContractEntry entry)
    {
        if (entry.Kind != JsonPropertyKind.Ref)
            return RefTargetsOfKind(entry.Type, entry.Kind);

        return _properties
            .DeclarationOrderPropertiesOf((INamedTypeSymbol)entry.Type)
            .SelectMany(property => RefTargetsWithin(property.Property.Type));
    }

    /// <summary>
    /// The object types a descriptor of <paramref name="type"/> points at
    /// through <c>ref</c>, recursing through the container shapes.
    /// </summary>
    private static IEnumerable<ITypeSymbol> RefTargetsWithin(ITypeSymbol type) =>
        RefTargetsOfKind(type, JsonTypeClassifier.Classify(type));

    private static IEnumerable<ITypeSymbol> RefTargetsOfKind(
        ITypeSymbol type,
        JsonPropertyKind kind
    ) =>
        kind == JsonPropertyKind.Ref
            ? [type]
            : JsonTypeClassifier.ChildTypesOf(type, kind).SelectMany(RefTargetsWithin);
}
