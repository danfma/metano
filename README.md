# Metano

**Write shared domain code once in C#. Ship clean TypeScript today, with an
experimental Dart/Flutter backend on the same IR.**

Metano is a Roslyn-powered transpiler for teams that keep their backend in
.NET and want to share domain code with frontend or app targets. Its primary
target emits idiomatic TypeScript that fits normal JS tooling; an experimental
Dart/Flutter target is also available for validating the multi-backend
architecture.

[![NuGet](https://img.shields.io/nuget/v/Metano.svg)](https://www.nuget.org/packages/Metano/)
[![npm](https://img.shields.io/npm/v/metano-runtime.svg)](https://www.npmjs.com/package/metano-runtime)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Why Metano?

Most full-stack .NET products end up describing the same concepts twice:
entities, DTOs, enums, validation rules, small calculations, and JSON contracts.
Metano keeps C# as the source of truth and generates real target code, not only
declaration stubs. TypeScript is the mature target today; Dart is intentionally
documented as experimental while its coverage grows.

Use it when you want:

- **One domain model across backend and frontend** without hand-maintained type
  mirrors.
- **Behavior as well as shapes**: records, methods, operators, guards, LINQ,
  async, exceptions, and JSON serializer contexts.
- **Idiomatic generated code**: modules, classes, interfaces, branded
  primitives, string unions, normal imports, and target-specific output where
  the backend supports it.
- **Low runtime weight**: no .NET runtime in the browser, no global shim, only
  small helper imports when the emitted code needs them.
- **Native target workflow**: generated TypeScript packages work with Bun,
  Vite, Vitest, Biome, ESLint, bundlers, and source maps; the Dart backend
  emits `.dart` files consumed by the Flutter sample.

## What It Generates Today

```csharp
using Metano.Annotations;

[assembly: TranspileAssembly]
[assembly: EmitPackage("sample-todo")]

namespace SampleTodo;

[StringEnum]
public enum Priority { Low, Medium, High }

public record TodoItem(string Title, bool Completed = false, Priority Priority = Priority.Medium)
{
    public TodoItem ToggleCompleted() => this with { Completed = !Completed };

    public TodoItem SetPriority(Priority priority) => this with { Priority = priority };

    public override string ToString() => $"[{(Completed ? "x" : " ")}] {Title} ({Priority})";
}
```

```typescript
export const Priority = {
  Low: "low",
  Medium: "medium",
  High: "high",
} as const;

export type Priority = (typeof Priority)[keyof typeof Priority];

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
    return this.with({ priority: priority });
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

> Note: parts of the generated code were omitted for brevity and files got merged into this single example. The excluded
> part contains the imports, for example.

## Feature Highlights

- C# records, classes, structs, interfaces, delegates, inheritance, generics,
  overloads, extension methods, nullable types, async, exceptions, pattern
  matching, and nested types.
- TypeScript-friendly output controls such as `[StringEnum]`, `[PlainObject]`,
  `[Branded]`, `[Ignore]`, `[NoContainer]`, `[GenerateGuard]`, `[ObjectArgs]`,
  `[Optional]`, and `[Import]`.
- BCL mappings for collections, LINQ, `decimal`, `Guid`, temporal types,
  `BigInteger`, tasks, strings, math, and console output.
- Cross-project package generation with `[EmitPackage]`, generated barrels,
  npm dependency updates, and package-aware imports.
- `System.Text.Json` source-generation metadata emitted as a TypeScript
  `SerializerContext`.
- TypeScript and Dart/Flutter compiler targets built on a shared IR.

## Getting Started

The default walkthrough targets TypeScript, because that is the production path
and the one wired into `Metano.Build`.

Prerequisites: .NET SDK 10.0 preview, C# preview features, and a JS runtime such
as Bun.

```bash
dotnet add package Metano
dotnet add package Metano.Build
```

Point your C# project at a generated TypeScript package:

```xml
<PropertyGroup>
  <MetanoOutputDir>../my-domain-ts/src</MetanoOutputDir>
  <MetanoClean>true</MetanoClean>
</PropertyGroup>
```

Then build:

```bash
dotnet build
```

For manual runs:

```bash
dotnet tool install --global Metano.Compiler.TypeScript
dotnet metano-typescript -p path/to/YourProject.csproj -o path/to/output/src --clean
```

The full walkthrough is in [Getting Started](docs/getting-started.md).

For the experimental Dart backend:

```bash
dotnet run --project src/Metano.Compiler.Dart/ -- \
  -p samples/SampleCounterV1/SampleCounterV1.csproj \
  -o targets/flutter/sample_counter/lib/sample_counter \
  --clean
```

See the [Dart/Flutter roadmap](docs/roadmap/better-flutter-support.md) and the
[Flutter sample target](targets/flutter/sample_counter/).

## Documentation

| Start here | Purpose |
|---|---|
| [Documentation home](docs/README.md) | Map of the guides and references |
| [Getting started](docs/getting-started.md) | First TypeScript project, build integration, generated package flow |
| [Attribute reference](docs/attributes.md) | Every public annotation and when to use it |
| [BCL mappings](docs/bcl-mappings.md) | How standard C# types lower, with current tables focused on TypeScript |
| [Cross-project references](docs/cross-package.md) | Multi-project packages and generated imports |
| [JSON serialization](docs/serialization.md) | `JsonSerializerContext` support |
| [Architecture](docs/architecture.md) | Compiler pipeline, shared IR, TypeScript/Dart targets, and extension points |
| [Comparison](docs/comparison.md) | How Metano differs from Blazor, Bridge.NET, NSwag, and Fable |
| [ADRs](docs/adr/) | Design decisions behind the current architecture |

## Samples

- [HelloWorld](samples/HelloWorld/) — top-level statements and the smallest
  generated module.
- [SampleTodo](samples/SampleTodo/) — records, string enums, LINQ overloads, and
  JSON serializer context output.
- [SampleTodo.Service](samples/SampleTodo.Service/) — Hono service,
  cross-package imports, DTOs, and module entry points.
- [SampleIssueTracker](samples/SampleIssueTracker/) — richer domain model with
  branded IDs, aggregates, LINQ, inheritance, and repositories.
- [SampleOperatorOverloading](samples/SampleOperatorOverloading/) — value
  objects, operator overloads, exceptions, and `BigInteger`.
- [SampleCounterV1](samples/SampleCounterV1/) through
  [SampleCounterV5](samples/SampleCounterV5/) — UI-oriented counter variants
  covering MVP, MVU, component models, Inferno interop, SolidJS interop, and a
  related Dart/Flutter target.

Generated TypeScript lives under [targets/js](targets/js/). Experimental Dart
output is exercised by [targets/flutter/sample_counter](targets/flutter/sample_counter/).

## Contributing

Metano is young and moving quickly. Before changing compiler behavior, read the
[architecture guide](docs/architecture.md) and the relevant
[architecture decision records](docs/adr/).

Useful local checks:

```bash
dotnet run --project tests/Metano.Tests/
dotnet csharpier format .
bunx biome check .
```

Versions are computed from git tags with MinVer. Releases publish the NuGet
packages plus the `metano-runtime` npm package.

## Links

- Repository: [github.com/danfma/metano](https://github.com/danfma/metano)
- NuGet: [nuget.org/packages/Metano](https://www.nuget.org/packages/Metano/)
- npm runtime: [npmjs.com/package/metano-runtime](https://www.npmjs.com/package/metano-runtime)
- Issues and roadmap: [github.com/danfma/metano/issues](https://github.com/danfma/metano/issues)
