using System.Text.Json.Serialization;
using Metano.Annotations;

namespace SampleJsonContracts;

// The built-in converter writes the member name, the descriptor the [Name]
// text, so MS0037 is reported (#734).
[StringEnum]
[JsonConverter(typeof(JsonStringEnumConverter<Tone>))]
public enum Tone
{
    [Name("mild")]
    Mild,
    Sharp,
}
