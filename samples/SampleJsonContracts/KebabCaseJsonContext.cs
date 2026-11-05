using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
[JsonSerializable(typeof(Stats))]
public partial class KebabCaseJsonContext : JsonSerializerContext;
