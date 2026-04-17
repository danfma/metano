# SampleTodo.Service

A C# implementation of a REST service that transpiles to a working
[Hono](https://hono.dev) application on the TypeScript side. Demonstrates the full
end-to-end flow: **cross-project types**, **external JS module integration**, and
**modules with top-level code**.

## What this sample demonstrates

| Feature | C# | Purpose |
|---------|----|---------|
| Cross-package imports | `using SampleTodo;` on `TodoItem` / `Priority` | Types from `SampleTodo` flow through `[EmitPackage]`-based discovery |
| `[PlainObject]` DTOs | `[PlainObject] public record CreateTodoDto(...)` | Request/response shapes emit as TS `interface`, not classes |
| `[EmitInFile("todos")]` | Multiple types in one C# namespace → one `todos.ts` | Consolidates related types into a single file |
| `[Import]` for external JS modules | `[Import(name: "Hono", from: "hono", Version = "^4.6.0")]` on the `Hono` class | Declares a type that's provided by an npm package, not transpiled |
| `[Name]` method renames | `[Name("get")] public void Get(...)` | Maps C# PascalCase method names to JS camelCase |
| `[ExportedAsModule]` | Static `Program` class with `Main()` | Emits top-level functions instead of a class |
| `[ModuleEntryPoint]` | `public static void Main()` | Method body becomes top-level module code |
| `[ExportVarFromBody]` | `[ExportVarFromBody("app", AsDefault = true)]` | Promotes a local `var app = new Hono()` to a `default export` |
| `Guid.NewGuid().ToString()` | ID generation | Lowers to `crypto.randomUUID()` |
| LINQ `OrderBy`, `FirstOrDefault`, `Find` | `_items.OrderBy(...).ToList()` | Lowers to lazy LINQ chain from `metano-runtime` |
| Async handlers | `Func<IHonoContext, Task<IHonoContext>>` | `Task<T>` → `Promise<T>`, `await` → `await` |
| With-expression patches | `item with { Title = patch.Title }` | Partial updates via record copy |

## The Hono interop layer

The interesting bit is how `Hono` itself is represented in C# **without** being
transpiled. The `Js/Hono/` folder contains thin C# wrapper classes (`Hono`,
`IHonoContext`, `IHonoRequest`) decorated with `[Import(..., from: "hono")]`:

```csharp
[Import(name: "Hono", from: "hono", Version = "^4.6.0")]
public class Hono
{
    [Name("get")]
    public void Get(string path, Func<IHonoContext, IHonoContext> handler) =>
        throw new NotSupportedException();
    // ...
}
```

When the transpiler sees a reference to `Hono` in the generated code, it:

1. Doesn't emit a `hono.ts` file (it's external)
2. Emits `import { Hono } from "hono"` at the top of `program.ts`
3. Adds `"hono": "^4.6.0"` to the auto-generated `package.json#dependencies`

The C# `throw new NotSupportedException()` bodies are never executed — they exist
only to satisfy the C# compiler. The real implementations live in the actual `hono`
npm package.

## Files

- **`AssemblyInfo.cs`** — Marks the assembly with `[EmitPackage("sample-todo-service")]`.
- **`Program.cs`** — The main entry point. Wires up routes on a `Hono` instance.
  The entire `Main()` body becomes top-level module code via `[ModuleEntryPoint]`,
  and the `app` local variable is exported as `export default app` via
  `[ExportVarFromBody]`.
- **`Todos.cs`** — Contains `StoredTodo`, `CreateTodoDto`, `UpdateTodoDto` (as
  `[PlainObject]` DTOs) and `TodoStore` (the in-memory repository), all co-located
  in `todos.ts` via `[EmitInFile("todos")]`.
- **`Js/Hono/Hono.cs`** — C# facade for `Hono` from npm (`[Import]`).
- **`Js/Hono/IHonoContext.cs`** — C# facade for the Hono `Context` interface.

## Generated output

The transpiled TypeScript lives in [`../../targets/js/sample-todo-service/src`](../../targets/js/sample-todo-service/src/):

- `program.ts` — route wiring, top-level code, `export default app`
- `todos.ts` — `StoredTodo`, `CreateTodoDto`, `UpdateTodoDto` interfaces + `TodoStore` class
- `index.ts` — barrel file

The generated `package.json` has:

```json
{
  "dependencies": {
    "sample-todo": "workspace:*",
    "hono": "^4.6.0",
    "metano-runtime": "workspace:*"
  }
}
```

Note how `sample-todo` is pulled in automatically because `Todos.cs` references
`TodoItem` and `Priority` from that package.

## How to build

```bash
dotnet build samples/SampleTodo.Service/SampleTodo.Service.csproj
```

## How to test

```bash
cd js/sample-todo-service
bun install
bun run build
bun test
```

Should see **9 passing tests** that:

- Start the Hono app with `app.fetch(new Request(...))`
- Exercise every CRUD route (`GET /`, `GET /todos`, `GET /todos/:id`,
  `POST /todos`, `PATCH /todos/:id`, `DELETE /todos/:id`)
- Assert status codes (200, 201, 204, 404)
- Verify JSON payloads round-trip correctly through `[PlainObject]` types and the
  cross-package `TodoItem`

## Why this matters

This sample is the **smoke test for cross-language interop**. It proves that
Metano can:

1. Treat a C# project as a source of types AND a source of business logic
2. Let you wrap any npm package with a lightweight C# facade
3. Produce TypeScript that uses those npm packages as if they were native
4. Share domain types across multiple projects transparently

Real-world applications would build on this pattern to share DTOs, validation, and
query logic between a .NET backend API and a TypeScript frontend/BFF.
