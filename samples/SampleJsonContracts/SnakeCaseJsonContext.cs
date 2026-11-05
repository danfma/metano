using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(Stats))]
public partial class SnakeCaseJsonContext : JsonSerializerContext;
