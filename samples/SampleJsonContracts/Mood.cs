using System.Text.Json.Serialization;
using Metano.Annotations;

namespace SampleJsonContracts;

// The built-in converter writes the [JsonStringEnumMemberName] text, the
// descriptor the member name, so MS0037 is reported (#734).
[StringEnum]
[JsonConverter(typeof(JsonStringEnumConverter<Mood>))]
public enum Mood
{
    [JsonStringEnumMemberName("calm-x")]
    Calm,
    Loud,
}
