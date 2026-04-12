using System.Text.Json.Serialization;
using SampleTodo;

namespace SampleTodo.Service;

/// <summary>
/// Serialization context for the Hono service boundary. Registers all DTOs
/// that flow through HTTP requests/responses, including the cross-package
/// <see cref="TodoItem"/> from <c>sample-todo</c>.
///
/// <para>
/// <see cref="StoredTodo"/>, <see cref="CreateTodoDto"/>, and
/// <see cref="UpdateTodoDto"/> are <c>[PlainObject]</c> records — they lower
/// to TypeScript interfaces, so their specs use object-literal factories
/// instead of <c>new T(...)</c> and omit the <c>type</c> constructor reference.
/// </para>
///
/// <para>
/// <see cref="TodoItem"/> is a class-backed record from another package.
/// Registering it here follows the same convention as C#'s
/// <c>System.Text.Json</c>: every type that appears in a serializable graph
/// must be listed in <c>[JsonSerializable]</c>, regardless of which assembly
/// it lives in.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StoredTodo))]
[JsonSerializable(typeof(CreateTodoDto))]
[JsonSerializable(typeof(UpdateTodoDto))]
[JsonSerializable(typeof(TodoItem))]
public partial class JsonContext : JsonSerializerContext;
