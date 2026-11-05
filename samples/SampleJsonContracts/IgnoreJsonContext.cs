using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonSerializable(typeof(IgnBase))]
[JsonSerializable(typeof(IgnAbs))]
public partial class IgnoreJsonContext : JsonSerializerContext;
