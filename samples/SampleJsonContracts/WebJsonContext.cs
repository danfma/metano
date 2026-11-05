using System.Text.Json;
using System.Text.Json.Serialization;

namespace SampleJsonContracts;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Clip))]
[JsonSerializable(typeof(ImageClip))]
[JsonSerializable(typeof(IBase))]
[JsonSerializable(typeof(Nums))]
[JsonSerializable(typeof(SoftNums))]
[JsonSerializable(typeof(int))]
public partial class WebJsonContext : JsonSerializerContext;
