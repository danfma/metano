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
    JsonPolymorphismValidator polymorphisms
)
{
    private readonly JsonPropertyCollector _properties = properties;
    private readonly JsonContractAdmission _admission = admission;
    private readonly JsonPolymorphismValidator _polymorphisms = polymorphisms;

    public JsonContract Collect(IReadOnlyList<JsonContractRoot> roots)
    {
        var entries = new List<JsonContractEntry>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var referrers = new Dictionary<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var polymorphisms = new Dictionary<ITypeSymbol, JsonPolymorphism>(
            SymbolEqualityComparer.Default
        );
        var pending = new Queue<JsonContractEntry>();

        foreach (var root in roots)
            Enqueue(new JsonContractEntry(root.Type, root.Kind, root));

        while (pending.TryDequeue(out var current))
        {
            if (LinkedBaseOf(current) is { } baseType)
                Link(current, baseType);

            foreach (var target in RefTargetsOf(current).Where(_admission.HasObjectContract))
                Link(current, target);

            if (PolymorphismOf(current) is not { } polymorphism)
                continue;

            polymorphisms.Add(current.Type, polymorphism);

            foreach (var derivedType in DerivedTypesOf(polymorphism))
                Link(current, derivedType);
        }

        return new JsonContract(entries, referrers, polymorphisms);

        void Link(JsonContractEntry referrer, ITypeSymbol target)
        {
            referrers.TryAdd(target, referrer.Type);
            Enqueue(new JsonContractEntry(target, JsonPropertyKind.Ref, Root: null));
        }

        void Enqueue(JsonContractEntry entry)
        {
            if (!seen.Add(entry.Type))
                return;

            entries.Add(entry);
            pending.Enqueue(entry);
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
    /// non-polymorphic contract.
    /// </summary>
    private JsonPolymorphism? PolymorphismOf(JsonContractEntry entry) =>
        entry.Kind == JsonPropertyKind.Ref
            ? _polymorphisms.Validate((INamedTypeSymbol)entry.Type)
            : null;

    /// <summary>
    /// The registered types a polymorphic base links to, leaving out a
    /// self-registration (the base is already an entry).
    /// </summary>
    private static IEnumerable<ITypeSymbol> DerivedTypesOf(JsonPolymorphism polymorphism) =>
        polymorphism
            .Derived.Select(registration => registration.Type)
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
