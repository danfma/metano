using System.Text.Json.Serialization;

namespace SampleTodo;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TodoItem))]
public partial class JsonContext : JsonSerializerContext;
