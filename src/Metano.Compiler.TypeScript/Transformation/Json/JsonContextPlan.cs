namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// What the generated context emits: the member names, the property
/// collector, and the claimed entries in emission order.
/// </summary>
internal sealed record JsonContextPlan(
    JsonTypeInfoNames Names,
    JsonPropertyCollector Properties,
    IReadOnlyList<JsonContractEntry> Entries
);
