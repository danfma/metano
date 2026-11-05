using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonDerivedType(typeof(UFailY), "y")]
public record UFail;
