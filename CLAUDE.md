# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Metano is a C# → TypeScript transpiler powered by Roslyn. It reads C# projects, transforms annotated types into a TypeScript AST, and prints formatted .ts files. It includes a lazy LINQ runtime, specialized type checks for overload dispatch, and cross-project import resolution via `[EmitPackage]`.

## Commands

### .NET

```sh
dotnet build                                          # build entire solution
dotnet run --project tests/Metano.Tests/            # run tests (TUnit — use dotnet run, not dotnet test)
dotnet run --project tests/Metano.Tests/ -- \
  --coverage --coverage-output-format cobertura \
  --coverage-output coverage.cobertura.xml \
  --results-directory TestResults                     # run tests with code coverage
dotnet run --project src/Metano.Compiler.TypeScript/ -- \
  -p samples/SampleTodo/SampleTodo.csproj \
  -o targets/js/sample-todo/src --clean               # transpile SampleTodo to TypeScript
dotnet run --project src/Metano.Compiler.Dart/ -- \
  -p samples/SampleCounter/SampleCounter.csproj \
  -o targets/flutter/sample_counter/lib/sample_counter --clean  # transpile SampleCounter to Dart
dotnet csharpier .                                    # format C# code
```

TUnit on .NET 10 requires `dotnet run` instead of `dotnet test`.

### JavaScript/TypeScript (Bun)

```sh
cd targets/js/metano-runtime && bun run build             # TypeScript build (tsgo)
cd targets/js/metano-runtime && bun test                  # run runtime tests
cd targets/js/sample-todo && bun run build                # TS build of generated code
cd targets/js/sample-todo && bun test                     # end-to-end tests (18 tests)
cd targets/js/sample-issue-tracker && bun run build && bun test  # 65 tests
cd targets/js/sample-todo-service && bun run build && bun test   # 19 tests (cross-package + Hono CRUD + JSON serialization)
cd targets/js/sample-counter && bun run dev               # Vite + SolidJS counter MVP sample
```

Always use **Bun** — never npm, yarn, or pnpm.

## Architecture

```
Metano.slnx
├── src/
│   ├── Metano/                       # Attributes (Metano.Annotations) + BCL mappings (Metano.Runtime)
│   │   ├── Annotations/                 # 21 attribute classes for transpilation control
│   │   └── Runtime/                     # Declarative BCL → JS mappings (Lists, Dictionaries, Math, Temporal, Decimal, etc.)
│   ├── Metano.Compiler/              # Target-agnostic core library
│   │   ├── ITranspilerTarget.cs         # Interface every language target implements
│   │   ├── TranspilerHost.cs            # Orchestrates load → compile → target.Transform → write
│   │   ├── SymbolHelper.cs              # Target-agnostic Roslyn helpers (attribute readers, type checks)
│   │   └── Diagnostics/                 # MetanoDiagnostic + DiagnosticCodes (MS0001–MS0008)
│   ├── Metano.Compiler.TypeScript/   # TypeScript target (depends on the core)
│   │   ├── TypeScriptTarget.cs          # ITranspilerTarget adapter
│   │   ├── Commands.cs                  # CLI (ConsoleAppFramework) — `metano-typescript`
│   │   ├── Bridge/                      # IR → TS AST bridges (enums, interfaces, plain objects)
│   │   ├── PackageJsonWriter.cs         # Auto-generates package.json (imports/exports/dependencies)
│   │   ├── Transformation/              # 39 focused handlers (TypeTransformer, ExpressionTransformer, etc.)
│   │   └── TypeScript/AST + Printer.cs  # ~65 TS AST record types and the printer
│   └── Metano.Compiler.Dart/         # Dart/Flutter target (prototype — shape-only, no bodies yet)
│       ├── DartTarget.cs                # ITranspilerTarget adapter
│       ├── Commands.cs                  # CLI — `metano-dart`
│       ├── Bridge/                      # IR → Dart AST (enum, interface, class)
│       ├── Transformation/              # Dart transformer orchestrator
│       └── Dart/AST + Printer.cs        # Minimal Dart AST + printer
├── tests/
│   └── Metano.Tests/                 # 337 TUnit tests with inline C# compilation
│       └── Expected/                    # Expected .ts output files for golden tests
├── samples/
│   ├── SampleTodo/                      # Sample C# project for end-to-end validation
│   ├── SampleTodo.Service/              # Hono-based service sample (cross-package + [PlainObject] CRUD)
│   ├── SampleIssueTracker/              # Larger sample exercising LINQ, records, modules, overloads
│   └── SampleCounter/                   # Counter MVP sample (used by Vite + SolidJS consumer)
└── targets/                             # One subfolder per language target + its samples
    ├── js/                              # Bun workspace (TypeScript target)
    │   ├── metano-runtime/              # metano-runtime (HashCode, HashSet, LINQ, type checks)
    │   ├── sample-todo/                 # Generated TS from SampleTodo + bun tests (18)
    │   ├── sample-todo-service/         # Generated TS from SampleTodo.Service + bun tests (9)
    │   ├── sample-issue-tracker/        # Generated TS from SampleIssueTracker + bun tests (51)
    │   └── sample-counter/              # Vite + SolidJS MVP consumer of generated SampleCounter TS
    └── flutter/                         # Dart/Flutter target consumers
        └── sample_counter/              # Flutter app consuming generated Dart from SampleCounter
```

### Pipeline

C# source + Metano attributes → Roslyn SemanticModel → TypeScript AST → Printer → .ts files

The core (`Metano.Compiler`) is target-agnostic. Each language target (TypeScript today,
Dart/Kotlin in the future) is its own project that implements `ITranspilerTarget` and ships
its own AST, printer, and CLI tool.

### Cross-Project Type Discovery

When a C# project references another that declares `[assembly: TranspileAssembly]` +
`[assembly: EmitPackage("name")]`, the compiler automatically:
1. Discovers transpilable types from the referenced assembly
2. Resolves cross-package imports (`import { Foo } from "name/subpath"`)
3. Merges multiple names from the same file into a single import line
4. Adds the package to the consumer's `package.json#dependencies` with the correct version
5. Uses per-name `type` qualifier when mixing value and type-only imports

### Metano Annotations

All attributes live in the `Metano.Annotations` namespace inside the `src/Metano` project.

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[Transpile]` | Type | Marks type for transpilation |
| `[assembly: TranspileAssembly]` | Assembly | Transpiles all public types (opt-out with `[NoTranspile]`) |
| `[NoTranspile]` | Type | Excludes from transpilation |
| `[StringEnum]` | Enum | Generates TS string union instead of numeric enum |
| `[Name("x")]` | Any | Overrides name in TS output |
| `[Ignore]` | Member | Omits member from output |
| `[ExportedAsModule]` | Static class | Emits top-level functions instead of class |
| `[GenerateGuard]` | Type | Generates `isTypeName()` type guard function |
| `[ExportFromBcl]` | Assembly | Maps BCL type to JS package (with optional `Version`) |
| `[Import]` | Type/Method | Declares external JS module dependency (with optional `Version`, `AsDefault`) |
| `[Emit("$0.foo($1)")]` | Method | Inlines JS at call site with argument placeholders |
| `[InlineWrapper]` | Struct | Value wrapper that lowers to a branded primitive |
| `[NoEmit]` | Type | Discoverable in C# but no .ts file emitted (ambient/declaration-only) |
| `[ModuleEntryPoint]` | Method | Method body becomes top-level executable code in the module |
| `[ExportVarFromBody]` | Method | Promotes a local var from the entry point to a module export |
| `[PlainObject]` | Record/Class | Emits as TS interface (no class wrapper); `new T(args)` → object literal |
| `[EmitPackage]` | Assembly | Declares the npm package identity for cross-project imports (with optional `Version`) |
| `[EmitInFile("name")]` | Type | Co-locates multiple types in a single .ts file |
| `[MapMethod]` | Assembly | Declarative BCL method → JS method/template mapping |
| `[MapProperty]` | Assembly | Declarative BCL property → JS property/template mapping |

### Tests

Tests use `TranspileHelper.Transpile(csharpSource)` which compiles C# inline, runs the transformer, and returns `filename → TS content`. For cross-package tests, use `TranspileHelper.TranspileWithLibrary(libSource, consumerSource)`. Expected output files live in `tests/Metano.Tests/Expected/`.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| .NET SDK | 10.0 (C# 14, preview features) |
| Transpiler | Roslyn 5.3.0 (Microsoft.CodeAnalysis) |
| CLI | ConsoleAppFramework |
| Testing (.NET) | TUnit |
| Testing (TS) | bun:test |
| Formatting | CSharpier (.NET) |
| Runtime | metano-runtime (Bun/TypeScript) |
| Package management | Central Package Management (Directory.Packages.props) |

## Build Configuration

- `global.json` — SDK 10.0.0, rollForward latestMinor
- `Directory.Build.props` — TreatWarningsAsErrors, ImplicitUsings, Nullable, LangVersion preview
- `Directory.Packages.props` — Central Package Management
- [GitHub issues](https://github.com/danfma/metano/issues) — feature backlog and in-flight work
- [`docs/adr/`](docs/adr/) — Architecture Decision Records (MADR-style, short format)

## Conventions

### Spec as source of truth

The product specification under `spec/` is the **single source of truth** for what
Metano should do. Every functional requirement (FR-NNN) and non-functional requirement
(NFR-NNN) in the spec is normative. The relationship between artifacts:

- `spec/` defines **what** the product must do (requirements, feature matrix, attributes, diagnostics).
- `docs/adr/` explains **why** specific architectural choices were made.
- GitHub issues track **concrete work** — each issue must reference a spec requirement or be a request to change the spec.

Rules:

- **New features** must have a corresponding FR in the spec before implementation begins. If the FR doesn't exist, create a spec PR first.
- **Bug fixes** reference the FR they correct (e.g., "Fix FR-007 static getter emission").
- **Exploratory work** (features not yet in the spec) is tracked as a spec change request — the issue proposes the new FR, and the spec is updated when the design is agreed.
- **The spec doesn't change without an issue/PR** — all spec edits are traceable.
- **Implemented features not in the spec** are documentation debt — the spec must be updated retroactively.

### Workflow

- **Review before commit.** Always run the `simplify` skill (code review with 3 agents: reuse, quality, efficiency) on the diff BEFORE committing or declaring a feature complete. Fix findings first, then commit.
- **Worktree per issue.** Use `git worktree add ../Metano-issue-{N} -b <branch> main` for branch work. Never switch branches in the main working directory — other agents may be working there concurrently.
- **Commit references.** When working on a GitHub issue, add `(#N)` at the end of the commit title and `Closes #N` (or `Part of #N` for partial work) in the commit body.
- **No AI attribution in commits.** Never add Co-Authored-By or similar references unless explicitly allowed.
- **Issue titles are prose.** `Introduce a metano.json config file`, not `feat: introduce metano.json config file`. PR titles follow conventional-commit style (`feat:`, `fix:`, `chore:`, etc.) because GitHub reuses them as squash-merge commit messages.
- **Conventional commits.** `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`, with optional scopes and `!` for breaking changes. The description after the prefix must start with a verb in the **infinitive** form: `feat: add ...`, `fix: correct ...`, `chore: move ...` — never `feat: added ...` or `fix: fixes ...`.

### Code

- **Language.** All repository artifacts (code, comments, docs, commits, PR descriptions) in English. Conversations with the user can be in Portuguese.
- **Nullable types.** Keep `T | null = null` as the default representation for nullable C# types in generated TS. Don't auto-convert to `T?` (optional/undefined). The C# `null` maps to TS `null`, not `undefined`. Exception: `[PlainObject]` DTO fields use `field?: T` (optional) for the wire-shape use case.
- **JS tooling.** Always use **Bun** — never npm, yarn, npx, or pnpm.
- **JS test conventions.** Tests live in `test/` (sibling to `src/`), mirroring the source directory structure. Imports use `#/*` subpath aliases (e.g., `import { Foo } from "#/system/json"`). Test files use `.test.ts` suffix.
- **Diagrams.** Always use Mermaid for any diagram in markdown (README, docs/, ADRs, specs). Never ASCII art — it breaks in proportional fonts and can't be zoomed. Prefer GitHub-native syntax: `flowchart`, `sequenceDiagram`, `classDiagram`, `stateDiagram-v2`, `erDiagram`.
