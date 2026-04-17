# SampleTodo

A minimal C# todo-list library that demonstrates the **basic building blocks** of
Metano: records, string enums, method transpilation, LINQ, and method overloads.

This is the best starting point to see what Metano produces from a small, simple
domain model.

## What this sample demonstrates

| Feature | C# | Generated TS |
|---------|----|----|
| String enum | `[StringEnum] public enum Priority { Low, Medium, High }` | `const Priority = { Low: "low", Medium: "medium", High: "high" } as const` |
| `[Name("x")]` override | `[Name("low")] Low` | Value `"low"` instead of `"Low"` |
| Record with defaults | `public record TodoItem(string Title, bool Completed = false, Priority Priority = Priority.Medium)` | Class with `equals`, `hashCode`, `with`, default values |
| `with` expression | `this with { Completed = !Completed }` | `this.with({ completed: !this.completed })` |
| `override ToString()` | `public override string ToString() => $"[{...}] {Title}"` | `toString()` method with template literal |
| Primary constructor class | `public class TodoList(string name)` | Class with `name` field from constructor |
| `IReadOnlyList<T>` | `public IReadOnlyList<TodoItem> Items => _items` | `Iterable<TodoItem>` getter |
| LINQ queries | `_items.Count(i => !i.Completed)` | `.filter(i => !i.completed).length` |
| Method overloads | `Add(TodoItem)`, `Add(string)`, `Add(string, Priority)` | Single method with runtime dispatcher + private fast-path methods |
| Nullable return | `TodoItem? FindByTitle(...)` | `TodoItem \| null` return type |
| `JsonSerializerContext` | `[JsonSerializable(typeof(TodoItem))] class JsonContext : JsonSerializerContext` | `JsonContext extends SerializerContext` with lazy `TypeSpec` getters |

## Files

- **`AssemblyInfo.cs`** — Marks the assembly with `[TranspileAssembly]` and
  `[EmitPackage("sample-todo")]` so consumers can `import { TodoItem } from "sample-todo"`.
- **`Priority.cs`** — String enum with `[Name("x")]` overrides for wire format.
- **`TodoItem.cs`** — Record with methods and `ToString()` override.
- **`TodoList.cs`** — Primary constructor class with private state, computed
  properties, LINQ queries, and method overloads.
- **`JsonContext.cs`** — JSON serializer context (transpiles to TypeScript `SerializerContext`).

## Generated output

The transpiled TypeScript lives in [`../../targets/js/sample-todo/src`](../../targets/js/sample-todo/src/).
Key files:

- `priority.ts` — the string union type
- `todo-item.ts` — the class with `equals`, `hashCode`, `with`
- `todo-list.ts` — the class with the overloaded `add` method + dispatcher
- `json-context.ts` — the serializer context
- `index.ts` — barrel file re-exporting everything

The generated `package.json` has `"name": "sample-todo"` and is consumed by
`SampleTodo.Service` via a cross-package import.

## How to build

From the repository root:

```bash
# Build the C# project (auto-transpiles via Metano.Build integration)
dotnet build samples/SampleTodo/SampleTodo.csproj

# Or build everything
dotnet build
```

## How to test the generated code

```bash
cd js/sample-todo
bun install
bun run build
bun test
```

You should see **18 tests passing**, covering:

- Record equality and `hashCode`
- The `with` expression creating copies
- `toggleCompleted()` and `setPriority()` returning new instances
- `toString()` formatting
- `TodoList.add()` overload dispatch
- `findByTitle()` returning `null` for missing items
- LINQ-based `pendingCount` and `hasPending()`
- String enum value checks
