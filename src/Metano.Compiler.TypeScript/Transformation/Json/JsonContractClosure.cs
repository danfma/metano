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
/// The contract closure of a context: its entries in emission order, and for
/// every entry some <c>ref</c> descriptor or <c>base</c> link points at, the
/// first entry that points at it.
/// </summary>
internal sealed record JsonContract(
    IReadOnlyList<JsonContractEntry> Entries,
    IReadOnlyDictionary<ITypeSymbol, ITypeSymbol> Referrers
);

/// <summary>
/// Computes the contract closure of the <c>[JsonSerializable]</c> roots: the
/// roots in declaration order, followed by every object type reached through
/// their serializable properties (walking the same nullable / array / map /
/// hash-set shapes the descriptors emit), through their <c>base</c> link, or
/// through the descriptor of a value root (its element, map value or nullable
/// inner), in breadth-first discovery order. Like System.Text.Json, the
/// context therefore carries a spec for every <c>ref</c> descriptor and
/// <c>base</c> link it emits. Entries are deduplicated by type symbol, so
/// cycles (<c>A → B → A</c>) terminate and each type is emitted once; name
/// collisions between distinct types are resolved afterwards by
/// <see cref="JsonTypeInfoNames"/>.
/// </summary>
internal sealed class JsonContractClosure(
    JsonPropertyCollector properties,
    JsonContractAdmission admission
)
{
    private readonly JsonPropertyCollector _properties = properties;
    private readonly JsonContractAdmission _admission = admission;

    public JsonContract Collect(IReadOnlyList<JsonContractRoot> roots)
    {
        var entries = new List<JsonContractEntry>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var referrers = new Dictionary<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<JsonContractEntry>();

        foreach (var root in roots)
            Enqueue(new JsonContractEntry(root.Type, root.Kind, root));

        while (pending.TryDequeue(out var current))
        {
            if (LinkedBaseOf(current) is { } baseType)
                Link(current, baseType);

            foreach (var target in RefTargetsOf(current))
            {
                if (seen.Contains(target) || _admission.IsDiscoverable(target))
                    Link(current, target);
            }
        }

        return new JsonContract(entries, referrers);

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
    /// The object types an entry's <c>ref</c> descriptors point at: for an
    /// object contract, the ones its properties reach; for a value root, the
    /// ones its own descriptor reaches.
    /// </summary>
    private IEnumerable<ITypeSymbol> RefTargetsOf(JsonContractEntry entry)
    {
        if (entry.Kind != JsonPropertyKind.Ref)
            return RefTargetsOfKind(entry.Type, entry.Kind);

        return _properties
            .PropertiesOf((INamedTypeSymbol)entry.Type)
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
