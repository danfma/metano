using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(Clip))]
public partial class OutOfOrderJsonContext : JsonSerializerContext;
