using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// What the generated context emits: the member names, the property
/// collector, the claimed entries in emission order, the polymorphism of
/// every polymorphic object contract, and the context's read options.
/// </summary>
internal sealed record JsonContextPlan(
    JsonTypeInfoNames Names,
    JsonPropertyCollector Properties,
    IReadOnlyList<JsonContractEntry> Entries,
    IReadOnlyDictionary<ITypeSymbol, JsonPolymorphism> Polymorphisms,
    JsonContextReadOptions ReadOptions
);
