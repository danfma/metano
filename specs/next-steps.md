# MetaSharp — Next Steps

## Concluído ✅

### Core

- [x] Records → classes com `constructor(readonly ...)`, `equals()`, `hashCode()`, `with()`
- [x] Enums → string union (`[StringEnum]`) ou numeric enum
- [x] Interfaces C# → interfaces TS (com `[Transpile]`)
- [x] `implements` em classes (interfaces transpiladas)
- [x] Herança (`extends`) com `super()` e constructor params corretos
- [x] Exceções → `class extends Error` com `super(message)`
- [x] Operadores → `static __op()` + `$op()` instance helper (binários e unários)
- [x] `[ExportedAsModule]` → static class vira funções top-level
- [x] Modificadores de acesso: `private`, `protected` (public omitido por padrão)
- [x] Async/await: `Task<T>` e `ValueTask<T>` → `Promise<T>`
- [x] BCL mappings: Math, string methods, Console.WriteLine
- [x] Namespaces → pastas + `index.ts` barrel re-exports
- [x] Imports type-only vs value (detecta `new` expressions)
- [x] `@meta-sharp/runtime` com `HashCode` (xxHash32)

### Atributos

- [x] `[Transpile]` — marca tipo para transpilação
- [x] `[StringEnum]` — enum como string union
- [x] `[Name("x")]` — override de nome no output
- [x] `[Ignore]` — omite membro
- [x] `[ExportedAsModule]` — static class → funções top-level

### Atributos

- [x] `[Transpile]`, `[StringEnum]`, `[Name]`, `[Ignore]`, `[ExportedAsModule]`
- [x] `[ExportFromBcl]` — assembly-level BCL → package JS
- [x] `[Import]` — tipo/membro externo de módulo JS (com `AsDefault` para default-import)
- [x] `[Emit]` — JS inline com placeholders
- [x] `[NoEmit]` — tipo declaration-only, descoberto mas não emitido (ambient)
- [x] `[ModuleEntryPoint]` — body do método vira top-level executável do módulo
- [x] `[ExportVarFromBody]` — promove local var do entry point a export do módulo
- [x] `[EmitPackage(name, target)]` + `EmitTarget` enum — identidade do package no target
- [x] `[EmitInFile("name")]` — co-locate múltiplos tipos num mesmo arquivo `.ts`

### AST & Printer

- [x] Generics na AST: `TsTypeParameter`, `TsNamedType.TypeArguments`
- [x] `TsClass.Extends` / `Implements` como `TsType` (não string)
- [x] `TsUnaryExpression` para operadores unários
- [x] `TsTupleType` para KeyValuePair/Tuple
- [x] `IndentedStringBuilder` extraído do Printer
- [x] Printer refatorado: `PrintConstructor`, `PrintClassMember`, `PrintBody`, `PrintAccessibility`

---

## Compiler Refactor (Done ✅)

> Large architectural refactor, in two phases, to prepare the compiler for multiple targets
> and eliminate the "God Objects" `TypeTransformer` (2826 lines) and `ExpressionTransformer`
> (973 lines). All 22+ commits passed with **197 + 51 + 17 tests green** with no change in
> the generated output.

### Phase A — Core / TypeScript target split

- [x] `MetaSharp.Compiler` became a **target-agnostic** library (core)
- [x] `MetaSharp.Compiler.TypeScript` is the TypeScript target (depends on core)
- [x] `ITranspilerTarget` interface in core
- [x] `TranspilerHost.RunAsync(options, target)` orchestrates: load project → compile → target.Transform → write files
- [x] `TypeScriptTarget : ITranspilerTarget` adapts the existing pipeline
- [x] `MetaSharpDiagnostic` and `SymbolHelper` (target-agnostic part) moved to core
- [x] CLI tool renamed to `metasharp-typescript`
- [x] Each future target (Dart, Kotlin) will be its own CLI/project without touching the core

### Phase B — TypeScript target internal decomposition

#### `TypeTransformer.cs`: 2826 → **466 lines** (-83.5%)

Extractions (in the order they were done):

- `BarrelFileGenerator` — leaf-only `index.ts` generation
- `RecordSynthesizer` — `equals/hashCode/with/MakeSelfType`
- `TypeGuardBuilder` — `is{Type}` guards (`[GenerateGuard]`)
- `TypeCheckGenerator` — per-parameter runtime type checks (dispatchers)
- `OverloadDispatcherBuilder` — constructor + method overload dispatch + fast paths
- `ImportCollector` + `PathNaming` — referenced-type walker, import resolution, kebab-case + `#/` paths
- `TypeScriptTransformContext` — immutable shared state (compilation, maps, PathNaming, diagnostic reporter, ExpressionTransformer factory)
- `EnumTransformer` — enum + `[StringEnum]`
- `InterfaceTransformer` — interfaces
- `ExceptionTransformer` — classes that extend `Error`
- `InlineWrapperTransformer` — branded types + companion namespace
- `ModuleTransformer` — `[ExportedAsModule]` static class / extension methods / extension blocks
- `RecordClassTransformer` — record / struct / class catch-all (the biggest, ~700 lines extracted)

#### `ExpressionTransformer.cs`: 973 → **174 lines** (-82.1%)

Extracted handlers (each covers one sub-grammar):

- `PatternMatchingHandler` — `is` patterns + the pattern sub-grammar
- `SwitchHandler` — `switch` statement + `switch` expression (composes `PatternMatchingHandler`)
- `LambdaHandler` — simple/parenthesized lambdas
- `ObjectCreationHandler` — `new`, implicit `new`, `with` expression
- `LiteralHandler`, `IdentifierHandler`, `GenericNameHandler` — atoms
- `MemberAccessHandler` — `obj.Prop` / `Type.Member`
- `InvocationHandler` — invocations + `[Emit]` template + `BclMapper` dispatch
- `InterpolatedStringHandler` — `$"..."` → template literals
- `OptionalChainingHandler` — `x?.Prop`, `x?.Method()`
- `CollectionExpressionHandler` — C# 12 `[]` → array literal or `new HashSet()`
- `OperatorHandler` — binary, assignment, prefix unary, legacy `is Type`
- `StatementHandler` — statements (return, yield, if, throw, expr stmt, local decl, block, switch)
- `ThrowExpressionHandler` — throw in expression position (lowered to an IIFE)
- `ArgumentResolver` — named-to-positional argument resolution

#### Final architectural pattern

- **Each C# construct with lowering logic = one dedicated handler**
- Handlers take the parent `ExpressionTransformer` (or `TypeScriptTransformContext`) as a dependency
- Handlers compose with each other through lazy properties exposed on the parent (`Patterns`, `Switches`, etc. are `internal` when they need to be reached by other handlers)
- The parent becomes a thin router + shared state
- Inline cases are kept only for trivial 1-line AST node renames with no logic (parenthesized, ternary, cast, await, this, element access)

#### Combined metrics

| File | Start | End | Δ |
|---|---|---|---|
| `TypeTransformer.cs` | 2826 | 466 | -83.5% |
| `ExpressionTransformer.cs` | 973 | 174 | -82.1% |
| **Total** | **3799** | **640** | **-83.2%** |

30+ new files in `MetaSharp.Compiler.TypeScript/Transformation/` + 8 new files in the core.

---

## Alta Prioridade

### ~~Value Wrappers — `[InlineWrapper]` attribute~~ ✅
> Structs que encapsulam um único valor primitivo (ex: `UserId`, `IssueId`) geram classes
> completas com equals/hashCode/with, mas no TS isso é overhead desnecessário.
>
> **Naming recomendado:** `[InlineWrapper]`.
>
> **Proposta:** `[InlineWrapper]` marca a struct para gerar um **branded type + companion namespace**:
>
> ```csharp
> [Transpile, InlineWrapper]
> public readonly struct UserId
> {
>     public string Value { get; }
>     public UserId(string value) { Value = value; }
>     public static UserId New() => new(Guid.NewGuid().ToString("N"));
>     public static UserId System() => new("system");
> }
> ```
>
> Gera:
> ```typescript
> export type UserId = string & { readonly __brand: "UserId" };
>
> export namespace UserId {
>     export function create(value: string): UserId { return value as UserId; }
>     export function newId(): UserId { return crypto.randomUUID().replace(/-/g, "") as UserId; }
>     export function system(): UserId { return "system" as UserId; }
> }
> ```
>
> **Vantagens:** Type safety via branded types (UserId ≠ IssueId), zero overhead em runtime,
> tree-shakeable, idiomático em TS.
>
> **Detecção:** Struct com `[InlineWrapper]` e exatamente 1 campo primitivo.
- [x] Definir atributo `[InlineWrapper]` no namespace `MetaSharp.Annotations`
- [x] Detectar no TypeTransformer: struct com `[InlineWrapper]` + 1 campo → branded type
- [x] Gerar `type X = primitive & { readonly __brand: "X" }` + namespace com static methods
- [x] Constructor → `create()` function no namespace
- [x] Static methods → funções no namespace
- [x] `instanceof` checks → `typeof` check no overload dispatcher
- [x] Non-string primitives: `toString()` helper gerado automaticamente
- [x] JS reserved words escapados no ToCamelCase (ex: `New` → `new_`)
- [x] Fallback: struct com >1 campo → class normal
- [x] SampleIssueTracker: UserId/IssueId como `readonly record struct` + `[InlineWrapper]`
- [x] Plano detalhado: `specs/value-wrappers-plan.md`

### ~~Generics~~ ✅

- [x] Record genérico simples (`Result<T>`) com `Partial<Result<T>>` no `with()`
- [x] Múltiplos type params (`Pair<K, V>`)
- [x] Constraints (`where T : IEntity` → `T extends IEntity`) com import do constraint
- [x] Herança genérica (`Ok<T> extends Result<T>`) com `super()` usando argumentos reais da base
- [x] Interface genérica (`IRepository<T>`)
- [x] Método genérico (`T Identity<T>(T value)`) e method constraints
- [x] Nested generics (`List<int>` → `number[]`)
- [x] `Partial<T>` estrutural (sem hack de string)
- [x] Implements genérico (`implements IContainer<T>`)
- [x] Constructor de classes derivadas: só params próprios, `super()` com args explícitos

### ~~Nullable Types~~ ✅

- [x] `int?` / `bool?` / `Nullable<T>` → `number | null`, `boolean | null`
- [x] `string?` / `CustomType?` → `string | null`, `Type | null` (nullable reference types)
- [x] `T?` onde `T : class` → `T | null` (generic nullable)
- [x] Nullable em return types e parâmetros
- [x] Null-conditional: `x?.Prop` → `x?.prop` (optional chaining)
- [x] Null-coalescing: `x ?? y` → `x ?? y`
- [ ] Futuro: `[Optional]` attribute para opt-in em `param?: T` (null vs undefined — manter `T | null = null` como default seguro para APIs)

### ~~Mapeamento de Tipos Externos~~ ✅

- [x] Temporal API: `DateTime` → `Temporal.PlainDateTime`, `DateOnly` → `Temporal.PlainDate`, `TimeOnly` →
  `Temporal.PlainTime`, `DateTimeOffset` → `Temporal.ZonedDateTime`, `TimeSpan` → `Temporal.Duration` (com import
  automático de `@js-temporal/polyfill`)
- [x] `Dictionary<K,V>` → `Map<K, V>`, `HashSet<T>` → `HashSet<T>` (runtime, com equals/hashCode)
- [x] `Guid` → `string`, `Uri` → `string`, `object` → `unknown`
- [x] `Tuple<T1,T2>` / `ValueTuple` / `KeyValuePair<K,V>` → `[T1, T2]` (TsTupleType)
- [x] `[ExportFromBcl]` — assembly-level: mapeia tipo BCL para package JS (ex: `decimal` → `Decimal` de `decimal.js`)
- [x] `[Import]` — tipo externo não gera .ts, referências geram import correto
- [x] `[Emit]` — JS inline nos call sites com placeholders `$0`, `$1`
- [ ] Config file (`meta-sharp.json`) — futuro, para mapeamentos sem poluir o código

### Cross-Project References ✅ (ProjectReference path)

**Status:** the `ProjectReference` flow is done. A library project decorated with
`[assembly: TranspileAssembly]` + `[assembly: EmitPackage("name")]` exposes its public
types to consumers; consumer projects automatically resolve cross-assembly type
references and emit `import { Foo } from "name/sub/path"` statements computed against
the **library's own** root namespace (so two libraries with overlapping namespaces
don't collide). Disambiguation happens at the symbol level via Roslyn, not by string
name, so two assemblies with same-named types are correctly distinguished.

**Done:**

- [x] `[assembly: EmitPackage(name, target = JavaScript)]` + `EmitTarget` enum (multiple
      instances allowed, one per target)
- [x] `PackageJsonWriter` becomes the authoritative source for `package.json#name` —
      diverging values get MS0007 warning, attribute wins
- [x] `TypeTransformer.DiscoverCrossAssemblyTypes` walks `compilation.References` and
      registers their public types into `_crossAssemblyTypeMap` (keyed by symbol identity)
- [x] `[Import]` declarations from referenced assemblies fold into the local
      `_externalImportMap` (transitive external bindings)
- [x] `TsNamedType` carries optional `TsTypeOrigin(PackageName, SubPath, IsDefault)`,
      populated by `TypeMapper` at construction time
- [x] `ImportCollector` consumes the origin directly during its AST walk and emits the
      cross-package import statement
- [x] `[Import(..., AsDefault = true)]` + `TsImport.IsDefault` for default-import syntax

**Follow-ups (not blocking):**

- [x] **MS0007 escalation in the consumer** — when a referenced type lives in an
      assembly with `[TranspileAssembly]` but no `[EmitPackage(JavaScript)]`, the
      compiler now emits MS0007 (hard error) at the consumer site instead of silently
      skipping the import. Diagnostics deduplicate by type display name so a single
      missing attribute produces exactly one error per type referenced.
- [x] **Auto-dependencies generation** in `package.json`: cross-package imports are now
      merged into the consumer's `package.json#dependencies` automatically. Three paths
      contribute:
      - **Cross-assembly types via `[EmitPackage]`**: version comes from
        `IAssemblySymbol.Identity.Version` formatted as `^Major.Minor.Patch`, falling
        back to `workspace:*` when the source assembly has no explicit version (sibling
        projects in a Bun monorepo).
      - **External types via `[Import("name", from: "pkg", Version = "^x.y.z")]`**:
        the version comes from the attribute. Without `Version`, the type still
        imports correctly but no auto-dep entry is created (the user adds it manually).
      - **BCL types via `[ExportFromBcl(..., Version = "^x.y.z")]`**: same model as
        `[Import]`. The default `decimal` mapping in `MetaSharp/Runtime/Decimal.cs`
        ships with `Version = "^10.6.0"`, so any consumer that uses `decimal` gets
        `decimal.js` in its dependencies automatically.

      The merge preserves user-hand-written entries for unrelated packages. Same-key
      entries are overwritten with the compiler-tracked version (the C# project is the
      source of truth).
- [x] **Multi-type-per-file support** via `[EmitInFile("name")]`. Types decorated with
      the same file name (in the same C# namespace) are co-located in one `.ts` file
      instead of producing one file per type. Cross-package consumers automatically
      resolve the import to the file path: a reference to a co-located type becomes
      `import { Foo, Bar } from "<package>/<ns>/<file>"`, and multiple names from the
      same file are merged into one import line. Conflicting namespaces under the same
      file name are rejected with MS0008.

### NuGet Library Path — `.metalib` (future)

The `ProjectReference` flow above uses Roslyn's in-process compilation references and
works for source-available libraries within the same solution. For libraries shipped
as NuGet packages (no source), MetaSharp needs a separate metadata sidecar file:

- A `.metalib` (binary metadata) embedded in the NuGet package
- Contains type signatures + namespace → package JS mapping + type guard info
- Read by the consumer's compiler when resolving cross-assembly references
- Format TBD: JSON, MessagePack, or MemoryPack

**Tasks:**

- [ ] Define `.metalib` schema (signatures + metadata)
- [ ] Generate `.metalib` during transpilation
- [ ] Embed `.metalib` in the NuGet package via `.csproj` targets
- [ ] Read `.metalib` from referenced packages and feed it into the same
      `_crossAssemblyTypeMap` as the `ProjectReference` path

**Futuro:**

- [ ] Compiler plugins para targets customizados (SolidJS JSX, React, etc.)
- [ ] Source maps cross-project

---

## Validação e Verificação de Tipos em Runtime

### ~~Type Guards / Shape Validation~~ ✅

- [x] `isMoney(value: unknown): value is Money` — standalone function por tipo
- [x] instanceof fast path + shape validation fallback
- [x] Records/classes: valida todos os campos (typeof para primitivos, guard recursivo para tipos transpilados)
- [x] StringEnum: valida contra literals (`value === "BRL" || ...`)
- [x] Numeric enum: valida typeof number + valores
- [x] Interfaces: shape-only (sem instanceof)
- [x] Nullable fields: `v.field == null || <inner check>`
- [x] Herança: valida campos da base + próprios
- [x] Exceções e ExportedAsModule: sem guard
- [x] Cross-file guard imports automáticos (`isCurrency` importado em Money.ts)
- [x] Guards re-exportados nos index.ts
- [x] `TsTypePredicateType` AST node para `value is TypeName`
- [x] `--guards` CLI flag
- [ ] Futuro: `assertMoney(value: unknown): Money` que throws
- [ ] Futuro: `fromJSON()` com validação + instanciação
- [ ] Futuro: discriminated unions (enum field como discriminante)

### ~~Overload de Construtores~~ ✅

- [x] Detectar múltiplos construtores via `type.Constructors`
- [x] Gerar TS overload signatures + dispatcher body
- [x] Checks inline com runtime type checks (`isInt32`, `isString`, `instanceof`, etc.)
- [x] Single constructor → sem dispatcher (backward compatible)
- [x] `TsConstructorOverload` AST node + Printer
- [ ] Factories auxiliares (`fromXY`, `fromPoint`) — futuro
- [ ] Herança com overloads (`super(...args)` no dispatcher) — futuro

### ~~Overload de Métodos (Method Dispatch)~~ ✅

**Abordagem implementada: dispatcher inline com type guards (slow path)**

- [x] Agrupa métodos por nome, gera overload signatures + dispatcher com `...args: unknown[]`
- [x] Runtime type checks especializados (isInt32, isString, instanceof, etc.)
- [x] Void methods: converte returns em expression statements + bare return
- [x] Return types diferentes: usa `unknown` como tipo comum do dispatcher
- [x] Static e instance methods suportados
- [x] **Fast path**: cada overload vira um método privado especializado (`addXY`, `addPoint`)
- [x] Dispatcher delega ao fast-path em vez de duplicar o body
- [x] Naming: nome + nomes dos params capitalizados; conflito → tipos primitivos como sufixo
- [ ] Futuro: chamadas estáticas (compile-time-known) gerar `obj.addXY()` direto, sem passar pelo dispatcher

---

## Média Prioridade

### Tracking — `SampleIssueTracker`

Plano detalhado em [sample-issue-tracker-plan.md](./sample-issue-tracker-plan.md).

- [x] Criar projeto `SampleIssueTracker` com separação em `Issues`, `Planning` e `SharedKernel`
- [x] Implementar contratos base: enums, IDs e records genéricos
- [x] Implementar domínio: `Issue`, `Comment`, `Sprint` e `IssueWorkflow`
- [x] Implementar aplicação: repositório em memória, service e queries exportadas como módulo
- [x] Gerar `js/sample-issue-tracker` e validar build com Bun (0 erros TS)
- [x] 22 bugs do transpiler corrigidos via SampleIssueTracker validation
- [ ] Testes end-to-end com Bun para lógica de negócio gerada

### ~~Properties com Getter/Setter Custom~~ ✅

- [x] Computed property (expression-bodied `=>`) → `get name(): Type { ... }`
- [x] Property com getter block → `get name(): Type { ... }`
- [x] Property com getter + setter → `get`/`set` pair
- [x] Non-constructor auto-property → `TsFieldMember` (`name: Type`)
- [x] Auto-property com initializer → `name: Type = value`
- [x] Readonly auto-property → `readonly name: Type`
- [x] `TsSetterMember` e `TsFieldMember` AST nodes + Printer
- [x] Primary constructor param detection (não confunde computed props com ctor params)

### ~~Switch / Pattern Matching~~ ✅

- [x] Switch statement (`switch/case/default`) → `switch` TS
- [x] Switch expression (`x switch { ... }`) → ternary chain
- [x] `is null` → `=== null`
- [x] `is not null` → `!(... === null)`
- [x] `is constant` → `=== value`
- [x] `is Type` → `instanceof Type` (class) ou `typeof x === "..."` (primitivo)
- [x] `is > 0` (relational) → `x > 0`
- [x] `is >= 0 and < 100` (combined) → `x >= 0 && x < 100`
- [x] `is 0 or 1` → `x === 0 || x === 1`
- [x] Property patterns (`is { Prop: value }`) → `x.prop === value`
- [x] Switch expression com relational patterns
- [x] `_` discard → default/else

### Extension Methods ✅ (parcial)

- [x] Classic extension methods (`this` param) → funções com receiver como primeiro parâmetro
- [x] Auto-detect: static classes com extension methods tratadas como módulo
- [x] Generic extension methods
- [x] Infraestrutura para C# 14 extension blocks (`HasExtensionMembers` + `TransformAsModule`)
- [x] C# 14 extension blocks — `TransformExtensionBlock` percorre syntax tree
- [x] C# 14 extension methods em blocos (com receiver auto-injetado)
- [x] C# 14 extension properties em blocos (gera função com receiver)

### ~~Collections Ricas~~ ✅

- [x] `List<T>` → `T[]`
- [x] `Dictionary<K,V>` → `Map<K,V>`
- [x] `HashSet<T>` → `HashSet<T>` (runtime, com equals/hashCode)
- [x] LINQ methods → Array methods (BclMapper) — API direta de coleção (.push, .includes, etc.)
- [x] LINQ runtime lazy (`EnumerableBase<T>` com hierarquia de classes compostas)
- [x] LINQ via `System.Linq.Enumerable` → `Enumerable.from(x).where(...)` lazy chains
- [x] Composição: where, select, selectMany, orderBy, orderByDescending, take, skip, distinct, groupBy, concat,
  takeWhile, skipWhile, distinctBy, reverse, zip, append, prepend, union, intersect, except
- [x] Terminais: toArray, toMap (ToDictionary), toSet (ToHashSet), first, firstOrDefault, last, lastOrDefault, single,
  singleOrDefault, any, all, count, sum, average, min, max, minBy, maxBy, contains, aggregate
- [x] Detecção automática: `IsLinqExtensionMethod` vs `IsCollectionType` via Roslyn semantic model
- [x] Anti-double-wrapping: `IsAlreadyLinqChain()` para chains compostas
- [x] `Queue<T>` → `T[]` (Enqueue→push, Dequeue→shift, Peek→[0])
- [x] `Stack<T>` → `T[]` (Push→push, Pop→pop, Peek→[arr.length-1])

### ~~Enums Avançados~~ ✅

- [x] Enum com métodos de extensão → funções auxiliares (via TransformAsModule)
- [x] `[Flags]` enum → numeric enum TS (bitwise nativo)
- [x] `HasFlag()` → `(value & flag) === flag`
- [x] `Enum.Parse<T>()` → `T[text as keyof typeof T]`

---

## Infraestrutura

### ~~Convenções de Output e Imports~~ ✅ (parcial)

> **Decisões:** kebab-case para arquivos, 1 tipo = 1 arquivo, barrels folha por namespace (sem
> agregadores pai), `#/` subpath imports para todos os imports cross-file, ciclic references não
> suportadas.

#### File names e estrutura
- [x] **kebab-case file names**: `UserId.cs` → `user-id.ts`
- [x] `SymbolHelper.ToKebabCase()` helper
- [x] `GetRelativePath` e `ComputeRelativeImportPath` geram paths kebab-case
- [x] 1 tipo = 1 arquivo

#### Barrels (folha apenas)
- [x] Barrel folha por pasta: re-exporta apenas os tipos da própria pasta
- [x] NÃO gera barrels agregadores pai
- [x] Detecta colisão `index.ts` (type chamado `Index`) e pula o barrel
- [x] StringEnum/InlineWrapper re-exportados como value (não type-only)
- [x] `"sideEffects": false` automaticamente no `package.json` gerado

#### Imports
- [x] **`#/` subpath imports** para todos os imports cross-file
- [x] Sample configurado com `package.json#imports` e `tsconfig#paths`
- [x] **Geração automática do `package.json`** com `imports`, `exports`, `sideEffects`, `type`
- [x] Conditional exports (`types`/`import`/`default`) apontando para dist com fallback src
- [x] Merge não-destrutivo: preserva user fields (name, deps, scripts)
- [x] CLI flags: `--package-root`, `--dist`, `--skip-package-json`

#### Estrutura de testes
- [x] Sample: testes em `test/` espelhando estrutura de `src/`
- [x] Sample: imports nos testes usam `#/`
- [x] Sample: `bunfig.toml` escopa testes para `./test`

#### Estética
- [x] Ordem dos membros da classe: fields → constructor → getters/setters → methods
- [x] Linhas em branco entre grupos de membros e top-level statements
- [x] Imports agrupados sem blank lines, blank line antes da primeira declaração

#### Pendentes
- [ ] **Cache incremental**: hash de arquivos + dependências semânticas, pular regeração
- [x] **Cyclic references**: detectar e emitir warning/error claro com a cadeia problemática
  (CyclicReferenceDetector emits MS0005 warnings)

### ~~Nested Types~~ ✅

> Implementado via **companion namespace** (declaration merging em TS).
> `class Outer { class Inner }` → `export class Outer; export namespace Outer { export class Inner }`

- [x] Detectar nested types no TypeTransformer (filtrados em `DiscoverTranspilableTypes`)
- [x] Companion namespace via `TsNamespaceDeclaration.Members`
- [x] Acesso `Outer.Inner` no call site (via `BuildQualifiedTypeName` no ExpressionTransformer)
- [x] Nested classes, nested enums, nested records suportados
- [x] Imports automáticos detectam o root type para nested references
- [x] 4 testes em NestedTypesTests.cs

### Compiler Architecture (recomendações Gemini)

> Análise completa em `specs/compiler-overview.md`. Foco em performance, manutenibilidade e robustez.

#### Performance & Escalabilidade
- [ ] **Paralelismo na transformação**: TypeTransformer processa tipos em paralelo
  (`Parallel.ForEach` ou `Dataflow`) — transpilação é embaraçosamente paralela
- [ ] **Compilação incremental**: cache de hashes (entrada + metadados Roslyn) para
  pular tipos não modificados — fundamental para projetos grandes e watch mode

#### Manutenibilidade
- [x] **Decomposition into focused handlers** (not the formal GoF Visitor pattern, but the same goal):
  `TypeTransformer` went from **2826 → 466 lines** (-83.5%) and `ExpressionTransformer` from
  **973 → 174 lines** (-82.1%). 30+ handlers extracted, each covering a specific sub-grammar
  (see the "Compiler Refactor (Done)" section above).
- [x] **Plugin/mapper system**: users register custom mappers without touching the core
  via assembly-level `[MapMethod]` / `[MapProperty]` attributes — see "Declarative
  mappings" below.

#### Diagnostics ✅
- [x] **Sistema de Diagnostics próprio** (`MetaSharpDiagnostic`): reporta warnings/errors
  com localização exata no source C# (Roslyn `Location`)
- [x] `/* unsupported: ... */` silencioso substituído por warnings + placeholder
- [x] Categorias: MS0001 UnsupportedFeature, MS0002 UnresolvedType, MS0003 AmbiguousConstruct, MS0004 ConflictingAttributes
- [x] CLI imprime em formato Roslyn-style com cores (yellow/red)
- [x] Errors causam exit code 1
- [x] 4 testes em DiagnosticsTests.cs

### Declarative BCL Mappings ✅

> Replaces the hardcoded `BclMapper.cs` with assembly-level `[MapMethod]` /
> `[MapProperty]` attributes living alongside the `MetaSharp` project under
> `MetaSharp/Runtime/`. The compiler walks every referenced assembly's attributes,
> indexes them by `(declaringType, memberName)`, and dispatches BCL → JS lowering
> through the registry. `BclMapper.cs` shrank from ~400 lines (with hardcoded type-name
> string matching) to ~340 lines of pure dispatch infrastructure.
>
> ```csharp
> [assembly: MapMethod(typeof(List<>), nameof(List<int>.Add), JsMethod = "push")]
> [assembly: MapProperty(typeof(List<>), nameof(List<int>.Count), JsProperty = "length")]
> [assembly: MapMethod(typeof(Enumerable), nameof(Enumerable.Where),
>     WrapReceiver = "Enumerable.from", JsMethod = "where")]
> ```

- [x] `[MapMethod]` / `[MapProperty]` attributes in the `MetaSharp.Annotations` namespace
- [x] `DeclarativeMappingRegistry` builds the lookup index from all referenced
  assemblies during `TypeTransformer.TransformAll` setup
- [x] `BclMapper.TryMap` / `TryMapMethod` consult the registry as the only source of
  truth for BCL lowering; no hardcoded type-name branches remain
- [x] External packages can ship their own mappings — any assembly with
  `[assembly: MapMethod]` declarations is picked up automatically
- [x] Default BCL mappings live under `MetaSharp/Runtime/`, organized by area:
  Lists, Strings, Math, Console, Guid, Tasks, Temporal, Enums, Queues, Stacks,
  Dictionaries, Sets, Linq (~140 declarations total)
- [x] Schema features:
    - `JsMethod` / `JsProperty` — simple rename, preserves the original receiver
    - `JsTemplate` — full template with `$this`, `$0`/`$1`/..., `$T0`/`$T1`/...
      (generic method type-arg names) placeholders, all expanded as real AST nodes
      via `TsTemplate` so nested calls / lambdas / binary operators round-trip cleanly
    - `WhenArg0StringEquals` — literal-argument filter for cases like
      `Guid.ToString("N")` vs the default `Guid.ToString()`
    - `WrapReceiver` — injects a wrapping call around the receiver (LINQ-style),
      with generic chain detection so long fluent chains only wrap once
    - `RuntimeImports` — declares runtime helper identifiers the template body
      references so the import collector can emit the appropriate
      `import { … } from "@meta-sharp/runtime";` line

#### Migration follow-ups

- [x] `ImmutableList<T>` / `ImmutableArray<T>` — lowered via `ImmutableCollection`
  namespace helpers in `@meta-sharp/runtime`. Each mutation method (Add, AddRange,
  Insert, Remove, RemoveAt, Clear) calls a namespaced pure function that returns a
  new array. No wrapper/class — representation stays as plain `T[]` so serialization
  works without friction (same approach as Kotlin's read-only collections).
- [x] `Dictionary<K,V>.TryGetValue` — expanded at the statement level into
  `const value = dict.get(key); if (value !== undefined) { … }`. The pattern is
  detected in `StatementHandler.TransformBody` so the `Transform` return type
  doesn't need to change.
- [x] `List<T>.Remove(item)` — lowered to `listRemove($this, $0)` runtime helper
  that does `indexOf + splice` and returns `bool`, matching the C# contract.

### Config File (`meta-sharp.json`)

- [ ] Output directory
- [ ] Type mappings globais
- [ ] Naming conventions (camelCase, PascalCase)
- [ ] Package name / scope
- [ ] Exclude patterns

### Watch Mode

- [ ] `--watch` flag na CLI
- [ ] FileSystemWatcher nos .cs do projeto
- [ ] Recompilação incremental (só arquivos alterados)
- [ ] Integração com Vite/Bun dev server

### Developer Experience

- [ ] Source maps (.cs → .ts) para debugging
- [ ] `--dry-run` para preview do output
- [ ] `--verbose` para log detalhado da transformação
- [ ] (Warnings para constructs não suportados está em "Compiler Architecture > Diagnostics")

### @meta-sharp/runtime

- [x] `HashCode` (xxHash32)
- [x] `HashSet<T>` com equals/hashCode customizado (system/collections/)
- [x] `dayNumber()` helper para DateOnly.DayNumber (temporal-helpers)
- [x] Runtime type checks: `isChar`, `isString`, `isByte`, `isSByte`, `isInt16`, `isUInt16`, `isInt32`, `isUInt32`,
  `isInt64`, `isUInt64`, `isFloat32`, `isFloat64`, `isBool`, `isBigInt`
- [x] `Decimal` integration via decimal.js — type mapping (`decimal` → `Decimal`), literal
  lowering (`1.5m` → `new Decimal("1.5")`), operator lowering (`a + b` → `a.plus(b)`),
  and member mappings (`decimal.Parse`, `CompareTo`, constants). Built-in via
  `MetaSharp/Runtime/Decimal.cs` with `Version = "^10.6.0"` for auto-deps.
- [x] `equals()` / `hashCode()` utilities — records auto-generate structural equality
  and xxHash32-based hashCode via `@meta-sharp/runtime`'s `HashCode` helper.
- [ ] Serialization helpers: `toJSON()` / `fromJSON()` com validação

### LINQ Runtime — Migração para pipe-based (tree-shaking)

> A implementação atual usa factory registration (`_registerFactories`) que puxa todos os
> operadores LINQ ao importar qualquer coisa do módulo. Isso impede tree-shaking.
>
> **Abordagem futura:** migrar para pipe-based (estilo RxJS), onde cada operador é uma função
> standalone importada individualmente. O bundler elimina operadores não utilizados.
>
> ```typescript
> // Atual (não tree-shakeable)
> Enumerable.from(items).where(x => x > 0).select(x => x * 2).toArray();
>
> // Futuro (tree-shakeable)
> import { from, pipe } from "@meta-sharp/runtime/linq";
> import { where, select, toArray } from "@meta-sharp/runtime/linq/operators";
> from(items).pipe(where(x => x > 0), select(x => x * 2), toArray());
> ```
>
> **Impacto no transpiler:** BclMapper geraria `pipe()` chains + imports granulares por operador.
> **Quando migrar:** quando o runtime crescer (serialização, type guards, etc.) ou tree-shaking
> se tornar requisito real. Refactor localizado (BclMapper + runtime, sem afetar C# do usuário).

- [ ] Definir tipo `OperatorFn<T, R>` e função `pipe()` com overloads tipados
- [ ] Converter cada operador para função standalone: `where(pred)` retorna `OperatorFn`
- [ ] `EnumerableBase.pipe(...operators)` aplica a cadeia
- [ ] BclMapper gera `pipe()` chains em vez de fluent calls
- [ ] Imports granulares no código gerado (um import por operador usado)
- [ ] Manter fluent API como sugar opcional para uso manual

---

## Bugs Conhecidos (encontrados via SampleTodo)

### ~~Lambda expressions~~ ✅

- [x] `SimpleLambdaExpression` → `(param) => { return expr; }`
- [x] `ParenthesizedLambdaExpression` → `(x, y) => { return expr; }`
- [x] Expression body e block body suportados
- [x] Async lambdas suportados
- [x] Type inference para parâmetros de lambda

### ~~Private fields~~ ✅

- [x] `IFieldSymbol` �� `TsFieldMember` com accessibility, readonly, initializer
- [x] Backing fields de auto-properties filtrados (sem duplicação)
- [x] C# 12 collection expressions: `[]` → `[]`

### ~~LINQ / Collection methods no BclMapper~~ ✅

- [x] `List<T>.Count` → `.length`, `Dictionary.Count` → `.size`
- [x] `List<T>.Add` → `.push`, `.Contains` → `.includes`, `.Clear` → `.length = 0`
- [x] `.Any()` → `.some()`, `.All()` → `.every()`, `.Where()` → `.filter()`
- [x] `.Select()` → `.map()`, `.SelectMany()` → `.flatMap()`
- [x] `.First()` → `[0]`/`.find()`, `.FirstOrDefault()` → `.find() ?? null`
- [x] `.Count(pred)` → `.filter(pred).length`
- [x] `.OrderBy()` → `.slice().sort()`, `.Take()` → `.slice(0,n)`, `.Skip()` → `.slice(n)`
- [x] `.ToList()`/`.ToArray()` → `.slice()`, `.Distinct()` → `Array.from(new Set())`
- [x] `.Sum()` → `.reduce()`, `.Min()`/`.Max()` → `Math.min/max`
- [x] `Dictionary.ContainsKey` → `.has()`, `.Add` → `.set()`, `.Remove` → `.delete()`

### ~~Method overloads de instância não detectados~~ ✅

- [x] `TodoList.Add(TodoItem)`, `Add(string)`, `Add(string, Priority)` → gera dispatcher com type guards
- [x] Mesmo pattern dos constructor overloads (overload signatures + dispatcher body)

---

## Futuro / Exploratório

### Serialização

- [ ] Imitar o serializador do C# no TS (JSON roundtrip com classes)
- [ ] `toJSON()` method gerado (class → plain object)
- [ ] `fromJSON()` static method gerado (plain object → class instance, com validação)
- [ ] Suporte a `[JsonPropertyName]`, `[JsonIgnore]`

### Other Targets

> The infrastructure is ready after the Compiler Refactor: each target is a separate project
> that implements `ITranspilerTarget` and has its own AST + Printer. The `MetaSharp.Compiler`
> core (target-agnostic) handles project loading + Roslyn compilation + diagnostics + file
> writes. Adding a new target = new project + new AST + new handlers, without touching the
> core or the existing TypeScript target.

- [ ] `MetaSharp.Compiler.Dart` — for use with Flutter
- [ ] `MetaSharp.Compiler.Kotlin` — for native Android
- [ ] **JSX/TSX target via plugin in the TypeScript target** (option B) — `[JsxComponent]` attribute
  + `JsxComponentTransformer` + new `Tsx*` AST nodes; emits raw `.tsx` and lets
  Vite/SWC/Babel resolve it with the framework's plugin (Solid, React, etc.)

### Lambda / Expression Tree Optimization

> Atualmente lambdas geram arrow functions TS diretamente. Para o futuro,
> considerar separar a representação de lambdas para permitir:

- [ ] Expression trees capturadas como objetos AST (similar a `Expression<Func<T>>` no C#)
- [ ] Visitor que traduz expression trees para SQL, GraphQL, etc. (IQueryable pattern)
- [ ] Build-time compilation: gerar um grande JS bundle a partir das árvores de expressão
- [ ] Otimizações: inline lambdas simples, fusing de .where().where(), etc.
- [ ] Avaliar se string + eval é viável para cenários dinâmicos (provavelmente não — segurança)

### Tooling

- [ ] Plugin para Rider/VS Code: highlight de tipos transpilados
- [ ] MSBuild integration: gerar TS no `dotnet build`
- [ ] NuGet package para distribuição do compiler como tool
