using Metano.Annotations;
using Metano.Annotations.TypeScript;

namespace SampleTodo.Service;

/// <summary>
/// Canonical kinds of events the todo service surfaces. <c>[StringEnum]</c>
/// lowers this to a TS string union so the discriminator narrowing
/// compiles to a literal comparison.
/// </summary>
[Transpile, StringEnum, EmitInFile("events")]
public enum TodoEventKind
{
    TodoCreated,
    TodoUpdated,
    TodoDeleted,
}

/// <summary>
/// Event published when a new todo is inserted. <c>[Discriminator]</c>
/// marks <c>Kind</c> as the narrowing field — the generated
/// <c>isTodoCreated</c> guard short-circuits on <c>v.kind !== "TodoCreated"</c>
/// before walking the rest of the shape, so a payload for a different
/// event exits the guard without scanning every field.
/// </summary>
[PlainObject, GenerateGuard, Discriminator("Kind"), EmitInFile("events")]
public record TodoCreated(TodoEventKind Kind, string Id, string Title);

/// <summary>
/// Event published when a todo is patched. Shares the same
/// <see cref="TodoEventKind"/> discriminator, different expected value
/// (<c>"TodoUpdated"</c>) — the emitted guard keyword matches the type
/// name by convention.
/// </summary>
[PlainObject, GenerateGuard, Discriminator("Kind"), EmitInFile("events")]
public record TodoUpdated(TodoEventKind Kind, string Id, string? Title, bool? Completed);
