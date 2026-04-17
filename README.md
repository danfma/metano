# Metano

**A C# → TypeScript transpiler powered by Roslyn.** Write your domain model,
DTOs, LINQ queries, and business logic in C#. Get idiomatic, fully-typed,
dependency-light TypeScript that runs in any modern JS environment.

[![NuGet](https://img.shields.io/nuget/v/Metano.svg)](https://www.nuget.org/packages/Metano/)
[![npm](https://img.shields.io/npm/v/metano-runtime.svg)](https://www.npmjs.com/package/metano-runtime)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## Why Metano?

If you have a C# backend and a TypeScript frontend, you end up maintaining the
same domain concepts twice: entities, enums, DTOs, validation rules, small
calculations. Keeping them in sync is a constant source of drift, bugs, and
friction.

Metano treats your C# code as the source of truth and generates the
TypeScript automatically — not hand-written type definitions, **actual working
TypeScript code**: classes with methods, records with `equals` / `hashCode` /
`with`, LINQ-style queries, pattern matching, type guards, and JSON
serialization.

It's designed for teams that want:

- **One source of truth** for domain types and logic shared across backend and frontend
- **Strong types** on both sides without manually syncing them
- **Idiomatic output** — real TypeScript you'd be proud to write
- **Zero runtime overhead** where possible (branded types, string unions, plain arrays)

---

## How Metano differs from previous C# → JS/TS tools

C# has a long history of "run .NET in the browser" projects — **Bridge.NET**,
**H5** (a Bridge fork), **SharpKit**, **JSIL**, and more recently **Blazor
WebAssembly**. They all solved variations of the same problem with trade-offs
Metano deliberately avoids.

**Bridge.NET and its descendants** ported a large chunk of the .NET BCL into
JavaScript so almost any C# code would just run. The output worked, but it
dragged in a heavy runtime, emitted code that looked nothing like hand-written
JavaScript, and made interop with the existing JS ecosystem awkward. You
ended up with "C# pretending to be JS".

**Blazor WebAssembly** ships the .NET runtime itself to the browser as a WASM
binary and runs the original C# IL. Full fidelity, but a multi-megabyte
download and painful interop with regular JS/TS code — you live in a separate
world from the rest of the frontend stack.

**API codegen tools** like NSwag and Swagger Codegen only generate
TypeScript *type declarations* from an OpenAPI contract. They solve the
schema sync problem but give you nothing on the behavior side: no methods,
no validation, no domain logic — just `interface` stubs.

**Fable** (F# → JS/TS) is the closest spiritual neighbor: idiomatic output,
ecosystem-aware, used in real products. Metano applies the same philosophy
to C#, which is the mainstream language for .NET backends.

**Metano's design bets:**

- **Share code *and* behavior, not just types.** Records, classes, methods,
  LINQ, pattern matching, exceptions — if it compiles in C#, the transpiler
  tries to give you real working TypeScript with the same semantics.
- **Output should be as good as hand-written TypeScript.** No global shim,
  no heavyweight runtime, no mangled names. The only runtime dependency is
  `metano-runtime`, a small npm package with the minimum viable helpers
  (HashCode, LINQ, HashSet, primitive type guards, optional JSON
  serializer).
- **Accept some restrictions, deliberately.** You opt types in explicitly
  (`[Transpile]` or `[assembly: TranspileAssembly]`), and the transpiler
  covers the language surface most teams use for domain code.
  Reflection-heavy code, dynamic dispatch, and unsafe blocks are out of
  scope on purpose — restricting the input keeps the output clean.
- **Zero runtime cost where possible.** `[StringEnum]` becomes a `const`
  object. `[InlineWrapper]` gives you branded primitives — `UserId` is
  literally a `string` at runtime. `[PlainObject]` emits plain interfaces so
  DTOs round-trip through `JSON.stringify` without ceremony.
- **Work *with* the JS ecosystem, not against it.** External npm packages
  are first-class: declare a C# facade with `[Import(from: "some-package")]`
  and the transpiler emits real `import` statements and wires up
  `package.json#dependencies` automatically.

Metano won't replace Blazor if you want full .NET in the browser, and it
won't replace NSwag if you only need type stubs from an API contract. It
fills the middle ground: **shared domain code between a .NET backend and a
TypeScript frontend, with clean output and no runtime penalty.**

---

## Features

### Language features

- **Records** → TS classes with `equals()`, `hashCode()`, `with()`, structural equality
- **Classes and inheritance** with `super()` calls, virtual methods, and overrides
- **Enums** → numeric enums OR string unions (`[StringEnum]`)
- **Interfaces** (including generic `IEntity<T>`) → TypeScript interfaces
- **Generics with constraints** (`where T : IEntity` → `T extends IEntity`)
- **Pattern matching** — `switch` statements and expressions, `is` patterns, property patterns
- **Nullable types** — both reference (`string?`) and value (`int?`) as `| null`
- **Async / await** — `Task<T>` / `ValueTask<T>` → `Promise<T>`
- **Exceptions** → `class extends Error`
- **Operators** (`==`, `+`, unary, etc.) → `__op` static helpers
- **Extension methods** (including C# 14 extension blocks)
- **Nested types** via companion namespace declaration merging
- **Method and constructor overloads** with runtime type dispatch

### Collections and LINQ

- `List<T>` / `IList<T>` → `T[]`
- `Dictionary<K,V>` → `Map<K,V>`
- `HashSet<T>` → custom `HashSet` with structural equality (from `metano-runtime`)
- `ImmutableList<T>` / `ImmutableArray<T>` → `T[]` with pure helper functions
- `Queue<T>` / `Stack<T>` → `T[]` with push / shift / pop
- **Full LINQ runtime** with lazy evaluation: `where`, `select`, `selectMany`,
  `orderBy`, `groupBy`, `distinct`, `take`, `skip`, `zip`, `union`, `intersect`,
  `except`, `aggregate`, `first`, `single`, `any`, `all`, and more

### BCL type mappings

- `DateTime` → `Temporal.PlainDateTime`
- `DateOnly` → `Temporal.PlainDate`
- `TimeOnly` → `Temporal.PlainTime`
- `DateTimeOffset` → `Temporal.ZonedDateTime`
- `TimeSpan` → `Temporal.Duration`
- `decimal` → `Decimal` (from `decimal.js`, arbitrary precision)
- `Guid` → `UUID` (branded `string`, from `metano-runtime`)
- `BigInteger` → `bigint`

### Control knobs via attributes

| Attribute                       | Purpose                                                |
| ------------------------------- | ------------------------------------------------------ |
| `[Transpile]`                   | Mark a type for transpilation                          |
| `[assembly: TranspileAssembly]` | Transpile all public types in the assembly             |
| `[NoTranspile]`                 | Exclude from transpilation                             |
| `[StringEnum]`                  | Emit enum as string union instead of numeric           |
| `[Name("x")]`                   | Rename type / member in TS output                      |
| `[Ignore]`                      | Omit member from output                                |
| `[InlineWrapper]`               | Struct → branded primitive (zero-cost type safety)     |
| `[PlainObject]`                 | Record / class → TS interface (no class wrapper)       |
| `[ExportedAsModule]`            | Static class → top-level functions                     |
| `[GenerateGuard]`               | Generate `isTypeName()` runtime type guard             |
| `[ModuleEntryPoint]`            | Method body becomes top-level module code              |
| `[EmitPackage("name")]`         | Declare npm package identity for cross-project imports |
| `[EmitInFile("name")]`          | Co-locate multiple types in one `.ts` file             |
| `[Import]` / `[ExportFromBcl]`  | Map C# type to an external JS module                   |
| `[Emit("$0.foo($1)")]`          | Inline JS at call sites with argument placeholders     |
| `[MapMethod]` / `[MapProperty]` | Declarative BCL method / property → JS mapping         |

Full reference in [`docs/attributes.md`](docs/attributes.md).

### Cross-project support

When one C# project references another that declares
`[assembly: EmitPackage("name")]`, Metano automatically discovers
transpilable types from the referenced assembly, resolves cross-package
imports as `import { Foo } from "name/sub/namespace"`, and adds the package
to the consumer's `package.json#dependencies` with the right version. See
[`docs/cross-package.md`](docs/cross-package.md).

### JSON serialization

Metano transpiles `System.Text.Json.Serialization.JsonSerializerContext`
subclasses into a TypeScript `SerializerContext` with pre-computed
`TypeSpec` definitions. JSON property names, naming policies (`CamelCase`,
`SnakeCaseLower`, etc.), and per-property overrides are all resolved at
compile time.

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TodoItem))]
public partial class JsonContext : JsonSerializerContext;
```

Becomes a TS `SerializerContext` with `JsonSerializer.serialize` /
`deserialize` that handles `Temporal` types, `Decimal`, `Map`, `HashSet`,
branded types, and nested objects transparently. See
[`docs/serialization.md`](docs/serialization.md).

---

## Quick example

**Input** — `samples/SampleTodo/TodoItem.cs`:

```csharp
using Metano.Annotations;

[assembly: TranspileAssembly]
[assembly: EmitPackage("sample-todo")]

namespace SampleTodo;

[StringEnum]
public enum Priority
{
    [Name("low")] Low,
    [Name("medium")] Medium,
    [Name("high")] High,
}

public record TodoItem(string Title, bool Completed = false, Priority Priority = Priority.Medium)
{
    public TodoItem ToggleCompleted() => this with { Completed = !Completed };
    public TodoItem SetPriority(Priority priority) => this with { Priority = priority };
    public override string ToString() => $"[{(Completed ? "x" : " ")}] {Title} ({Priority})";
}
```

**Output** — `targets/js/sample-todo/src/todo-item.ts`:

```typescript
import { HashCode } from "metano-runtime";
import { Priority } from "./priority";

export class TodoItem {
  constructor(
    readonly title: string,
    readonly completed: boolean = false,
    readonly priority: Priority = "medium",
  ) {}

  toggleCompleted(): TodoItem {
    return this.with({ completed: !this.completed });
  }

  setPriority(priority: Priority): TodoItem {
    return this.with({ priority });
  }

  toString(): string {
    return `[${this.completed ? "x" : " "}] ${this.title} (${this.priority})`;
  }

  equals(other: any): boolean {
    return (
      other instanceof TodoItem &&
      this.title === other.title &&
      this.completed === other.completed &&
      this.priority === other.priority
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.title);
    hc.add(this.completed);
    hc.add(this.priority);
    return hc.toHashCode();
  }

  with(overrides?: Partial<TodoItem>): TodoItem {
    return new TodoItem(
      overrides?.title ?? this.title,
      overrides?.completed ?? this.completed,
      overrides?.priority ?? this.priority,
    );
  }
}
```

And `targets/js/sample-todo/src/priority.ts`:

```typescript
export const Priority = {
  Low: "low",
  Medium: "medium",
  High: "high",
} as const;

export type Priority = (typeof Priority)[keyof typeof Priority];
```

---

## Getting started

**Prerequisites:** .NET SDK 10.0 (preview, pinned via `global.json`) and
Bun 1.3+. Metano uses C# 14 preview features.

The fastest path to a running setup:

```bash
# Add the transpiler attributes + build integration to your .csproj
dotnet add package Metano
dotnet add package Metano.Build

# Annotate a type and build — Metano.Build runs the transpiler automatically
dotnet build
```

Full walkthrough — creating a project, annotating types, consuming the
generated TypeScript from Bun — in
[`docs/getting-started.md`](docs/getting-started.md).

To transpile ad hoc without the MSBuild integration:

```bash
dotnet tool install --global Metano.Compiler.TypeScript
metano-typescript -p path/to/YourProject.csproj -o path/to/output/src --clean
```

---

## Documentation

| Guide                                                   | What it covers                                                               |
| ------------------------------------------------------- | ---------------------------------------------------------------------------- |
| [Getting started](docs/getting-started.md)              | First Metano project — create a csproj, annotate, build, consume from Bun   |
| [Attributes](docs/attributes.md)                        | Complete reference for every attribute with examples                         |
| [BCL mappings](docs/bcl-mappings.md)                    | Every C# → TypeScript type mapping (primitives, collections, temporal, etc.) |
| [Cross-package setup](docs/cross-package.md)            | Multi-project `[EmitPackage]` flow, auto-deps, MS0007 / MS0008 diagnostics   |
| [Serialization](docs/serialization.md)                  | `JsonSerializerContext` transpilation and runtime serializer                 |
| [Architecture](docs/architecture.md)                    | Internal pipeline, project split, TS target internals, extension points     |
| [Architecture Decision Records](docs/adr/)              | The "why" behind major design choices — 12 ADRs and counting                 |

---

## Samples

Real C# projects transpiled into TypeScript and exercised with Bun tests —
each one validates a different slice of the compiler:

- [`samples/SampleTodo`](samples/SampleTodo/) — minimal records + string
  enums. Good starting point. Generates
  [`targets/js/sample-todo`](targets/js/sample-todo/).
- [`samples/SampleTodo.Service`](samples/SampleTodo.Service/) — Hono CRUD
  service showing cross-package imports, `[PlainObject]` DTOs, and
  `[ModuleEntryPoint]`. Generates
  [`targets/js/sample-todo-service`](targets/js/sample-todo-service/).
- [`samples/SampleIssueTracker`](samples/SampleIssueTracker/) — larger
  domain model with branded IDs, rich aggregates, LINQ queries,
  inheritance, and a repository. Generates
  [`targets/js/sample-issue-tracker`](targets/js/sample-issue-tracker/).
- [`samples/SampleCounter`](samples/SampleCounter/) — Counter MVP
  (model + view interface + presenter) consumed by a Vite + SolidJS
  frontend in [`targets/js/sample-counter`](targets/js/sample-counter/).

---

## Contributing

Metano is young and actively evolving. Issues and pull requests are
welcome.

**Before sending a PR**

- For non-trivial changes, open an issue first so the direction can be
  discussed. Feature ideas, design alternatives, and bug reports all
  belong in the [issue tracker](https://github.com/danfma/metano/issues).
- Every new feature needs at least one test in `tests/Metano.Tests/` and
  ideally an end-to-end assertion in one of the samples.

**Branch naming**

`<type>/<short-kebab-description>`, where `<type>` is one of `feat`,
`fix`, `chore`, `docs`, `refactor`, `test`. Examples:
`feat/json-serializer-phase-1`, `fix/cyclic-barrel-detector`,
`docs/adr-0013-watch-mode`.

**Commit style**

Conventional commits. When the work relates to an issue, append `(#N)` to
the commit title and `Closes #N` (or `Part of #N` for partial work) in
the commit body:

```
feat: merge local imports per barrel (#12)

Refactor ImportCollector's local-type branch to bucket by path and emit
one merged TsImport per barrel, mirroring the cross-package strategy.

Closes #12
```

**Running everything locally**

```bash
# .NET tests (TUnit — use `dotnet run`, not `dotnet test`)
dotnet run --project tests/Metano.Tests/

# C# formatting (checked in CI)
dotnet csharpier format .

# JS runtime + samples (Bun workspace — always use Bun, never npm/yarn/pnpm)
cd js && bun install
cd metano-runtime && bun run build && bun test
cd ../sample-todo && bun run build && bun test
cd ../sample-todo-service && bun run build && bun test
cd ../sample-issue-tracker && bun run build && bun test

# JS formatting / linting
cd js && bunx biome check .
```

Husky pre-push hooks run CSharpier and Biome automatically; if CI fails
on a formatting check, run the tools locally and recommit.

**Deeper context**

- [`docs/architecture.md`](docs/architecture.md) — how the compiler is
  structured internally.
- [`docs/adr/`](docs/adr/) — the architectural decisions behind the
  current design. Read before proposing changes that touch the shape of
  the output or the core/target split.
- [`CLAUDE.md`](CLAUDE.md) — working conventions for AI-assisted
  contributions.

**Releases**

Trunk-based: everything lands on `main`, releases are cut by tagging
`main` with `vX.Y.Z`. Versions are computed by MinVer from git tags, so
there are no manual version bumps in `.csproj` or `package.json`. The
release workflow (`.github/workflows/release.yml`) publishes four NuGet
packages (`Metano`, `Metano.Compiler`, `Metano.Compiler.TypeScript`,
`Metano.Build`) and one npm package (`metano-runtime`), all sharing the
same version.

---

## License

MIT — see [`LICENSE`](LICENSE) if present, or the `PackageLicenseExpression`
metadata in `Directory.Build.props`.

---

## Links

- **Repository**: [github.com/danfma/metano](https://github.com/danfma/metano)
- **NuGet packages**: [nuget.org/packages/Metano](https://www.nuget.org/packages/Metano/)
- **npm package**: [npmjs.com/package/metano-runtime](https://www.npmjs.com/package/metano-runtime)
- **Issue tracker / roadmap**: [github.com/danfma/metano/issues](https://github.com/danfma/metano/issues)
