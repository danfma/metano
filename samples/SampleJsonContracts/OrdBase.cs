using System.Text.Json.Serialization;

namespace SampleJsonContracts;

public sealed record OrdBase(
    [property: JsonPropertyOrder(5)] int Second,
    int First,
    [property: JsonPropertyOrder(3)] int Third
);
