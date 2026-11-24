using System.Text.Json.Serialization;
using Metano.Annotations;

namespace SampleJsonContracts;

// The built-in converter over a [StringEnum] writes the member names the
// descriptor writes, so MS0037 stays quiet (#734).
[StringEnum]
[JsonConverter(typeof(JsonStringEnumConverter<Spice>))]
public enum Spice
{
    Mild,
    Hot,
}
