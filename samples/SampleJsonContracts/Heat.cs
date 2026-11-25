using System.Text.Json.Serialization;

namespace SampleJsonContracts;

// The same converter over a numeric enum: System.Text.Json writes the name,
// the descriptor writes the number, so MS0037 is reported (#734).
[JsonConverter(typeof(JsonStringEnumConverter<Heat>))]
public enum Heat
{
    Cool,
    Warm,
}
