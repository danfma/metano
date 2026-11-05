using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(SBase), "b")]
public record SBase(int V);
