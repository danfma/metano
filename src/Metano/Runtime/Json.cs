// Declarative BCL → JavaScript mappings for the static System.Text.Json serializer calls that
// take a source-generated context's JsonTypeInfo (#741): the `metano-runtime` `JsonSerializer`.
// `Serialize` writes JSON text as STJ's default options write it (`serializeText`: no
// whitespace, the default encoder's escapes, .NET's number text); `Deserialize(string, …)`
// reads it (`deserializeText`: exact decimal / 64-bit digits, `JsonException` for invalid
// text, `null` for the text `null`). A null text or type info throws ArgumentNullException.
// The overloads over options, Type, streams, writers, spans and JsonElement stay unmapped.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Metano.Annotations;

[assembly: MapMethod(
    typeof(JsonSerializer),
    nameof(JsonSerializer.Serialize),
    JsTemplate = "JsonSerializer.serializeText($0, $1)",
    RuntimeImports = "JsonSerializer",
    WhenParameterTypeNames = new[]
    {
        "TValue",
        "System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>",
    }
)]
[assembly: MapMethod(
    typeof(JsonSerializer),
    nameof(JsonSerializer.Serialize),
    JsTemplate = "JsonSerializer.serializeText($0, $1)",
    RuntimeImports = "JsonSerializer",
    WhenParameterTypes = [typeof(object), typeof(JsonTypeInfo)]
)]
[assembly: MapMethod(
    typeof(JsonSerializer),
    nameof(JsonSerializer.Deserialize),
    JsTemplate = "JsonSerializer.deserializeText($0, $1)",
    RuntimeImports = "JsonSerializer",
    WhenParameterTypes = [typeof(string), typeof(JsonTypeInfo<>)]
)]
[assembly: MapMethod(
    typeof(JsonSerializer),
    nameof(JsonSerializer.Deserialize),
    JsTemplate = "JsonSerializer.deserializeText($0, $1)",
    RuntimeImports = "JsonSerializer",
    WhenParameterTypes = [typeof(string), typeof(JsonTypeInfo)]
)]
