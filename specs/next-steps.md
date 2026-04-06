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
- [x] `[Import]` — tipo/membro externo de módulo JS
- [x] `[Emit]` — JS inline com placeholders

### AST & Printer

- [x] Generics na AST: `TsTypeParameter`, `TsNamedType.TypeArguments`
- [x] `TsClass.Extends` / `Implements` como `TsType` (não string)
- [x] `TsUnaryExpression` para operadores unários
- [x] `TsTupleType` para KeyValuePair/Tuple
- [x] `IndentedStringBuilder` extraído do Printer
- [x] Printer refatorado: `PrintConstructor`, `PrintClassMember`, `PrintBody`, `PrintAccessibility`

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
- [x] Definir atributo `[InlineWrapper]` no MetaSharp.Annotations
- [x] Detectar no TypeTransformer: struct com `[InlineWrapper]` + 1 campo → branded type
- [ ] Gerar `type X = primitive & { readonly __brand: "X" }` + namespace com static methods
- [ ] Constructor → `create()` function no namespace
- [ ] Static methods → funções no namespace
- [ ] `instanceof` checks → substituir por type guard customizado (opcional)
- [ ] Testes com `[InlineWrapper]` + SampleIssueTracker validation
- [ ] Plano detalhado: `specs/value-wrappers-plan.md`

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
- [ ] Futuro: distinguir `null` vs `undefined` para parâmetros opcionais

### ~~Mapeamento de Tipos Externos~~ ✅

- [x] Temporal API: `DateTime` → `Temporal.PlainDateTime`, `DateOnly` → `Temporal.PlainDate`, `TimeOnly` →
  `Temporal.PlainTime`, `DateTimeOffset` → `Temporal.ZonedDateTime`, `TimeSpan` → `Temporal.Duration` (com import
  automático de `@js-temporal/polyfill`)
- [x] `Dictionary<K,V>` → `Map<K, V>`, `HashSet<T>` → `Set<T>` (globais, sem import)
- [x] `Guid` → `string`, `Uri` → `string`, `object` → `unknown`
- [x] `Tuple<T1,T2>` / `ValueTuple` / `KeyValuePair<K,V>` → `[T1, T2]` (TsTupleType)
- [x] `[ExportFromBcl]` — assembly-level: mapeia tipo BCL para package JS (ex: `decimal` → `Decimal` de `decimal.js`)
- [x] `[Import]` — tipo externo não gera .ts, referências geram import correto
- [x] `[Emit]` — JS inline nos call sites com placeholders `$0`, `$1`
- [ ] Config file (`meta-sharp.json`) — futuro, para mapeamentos sem poluir o código

### Cross-Project References — `.metalib` (MetaSharp Library)

**Design aprovado:** opção 1 — arquivo binário de metadados (similar ao `.klib` do Kotlin).

O `.metalib` contém:

- Assinaturas dos tipos transpilados (contratos, não código)
- Metadata: namespace → package JS mapping, type guards info
- Formato: JSON, MessagePack ou MemoryPack (a definir)

**Fluxo:**

```
Projeto A (lib)
  → meta-sharp compile → gera .ts + .metalib
  → dotnet pack → NuGet com .metalib embutido

Projeto B (consome A via NuGet)
  → meta-sharp compile → lê .metalib de A → resolve tipos e gera imports
  → bundler (Vite/Bun) → resolve packages → bundle final
```

**Resolução de referências:**

- `ProjectReference` (source disponível) → Roslyn resolve direto
- `PackageReference` (NuGet, sem source) → lê `.metalib` do package

**Tarefas:**

- [ ] Definir formato do `.metalib` (schema de assinaturas, metadata)
- [ ] Gerar `.metalib` durante `meta-sharp compile`
- [ ] Embutir `.metalib` no NuGet package (via `.csproj` targets)
- [ ] Ler `.metalib` de packages referenciados para resolver tipos
- [ ] `ProjectReference A → B` gera import do package JS correspondente
- [ ] Gerar `package.json` com dependências entre projetos
- [ ] Cada assembly .NET → um package JS

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
- [ ] **Futuro — Fast path**: tipo conhecido em compile-time → chamada direta ao método com hash
    - Naming: `methodName` + nomes dos params (ex: `addXY`, `addPoint`)
    - Se conflito: `addNumberX_NumberY`, `addPointOther`
- [ ] Futuro: abordagem mista (dispatch estático + fallback dinâmico)

---

## Média Prioridade

### Tracking — `SampleIssueTracker`

Plano detalhado em [sample-issue-tracker-plan.md](./sample-issue-tracker-plan.md).

- [x] Criar projeto `SampleIssueTracker` com separação em `Issues`, `Planning` e `SharedKernel`
- [x] Implementar contratos base: enums, IDs e records genéricos
- [x] Implementar domínio: `Issue`, `Comment`, `Sprint` e `IssueWorkflow`
- [x] Implementar aplicação: repositório em memória, service e queries exportadas como módulo
- [ ] Gerar `js/sample-issue-tracker` e validar build/test com Bun
- [ ] Revisar output gerado para legibilidade e cobertura das features-alvo

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
- [ ] C# 14 extension blocks — testes inline pendentes (funciona via MSBuildWorkspace)
- [ ] C# 14 extension properties em blocos

### ~~Collections Ricas~~ ✅

- [x] `List<T>` → `T[]`
- [x] `Dictionary<K,V>` → `Map<K,V>`
- [x] `HashSet<T>` → `Set<T>`
- [x] LINQ methods → Array methods (BclMapper) — API direta de coleção (.push, .includes, etc.)
- [x] LINQ runtime lazy (`EnumerableBase<T>` com hierarquia de classes compostas)
- [x] LINQ via `System.Linq.Enumerable` → `Enumerable.from(x).where(...)` lazy chains
- [x] Composição: where, select, selectMany, orderBy, orderByDescending, take, skip, distinct, groupBy, concat,
  takeWhile, skipWhile, distinctBy, reverse, zip, append, prepend, union, intersect, except
- [x] Terminais: toArray, toMap (ToDictionary), toSet (ToHashSet), first, firstOrDefault, last, lastOrDefault, single,
  singleOrDefault, any, all, count, sum, average, min, max, minBy, maxBy, contains, aggregate
- [x] Detecção automática: `IsLinqExtensionMethod` vs `IsCollectionType` via Roslyn semantic model
- [x] Anti-double-wrapping: `IsAlreadyLinqChain()` para chains compostas
- [ ] `Queue<T>`, `Stack<T>` → arrays com helpers

### Enums Avançados

- [ ] Enum com métodos de extensão → funções auxiliares
- [ ] `[Flags]` enum → bitwise operations
- [ ] Enum parsing: `Enum.Parse<T>()` → lookup table

---

## Infraestrutura

### Mapeamentos Declarativos (substituir BclMapper hardcoded)

> Atualmente os mapeamentos BCL→JS estão hardcoded no `BclMapper.cs`.
> Migrar para um sistema declarativo via atributos assembly-level:

```csharp
[assembly: MapMethod(typeof(List<>), "Add", JsMethod = "push")]
[assembly: MapProperty(typeof(List<>), "Count", JsProperty = "length")]
[assembly: MapMethod(typeof(Enumerable), "Any", JsMethod = "some")]
```

- [ ] Definir atributos `[MapMethod]`, `[MapProperty]` no MetaSharp.Annotations
- [ ] Ler mapeamentos no TypeTransformer (similar a `[ExportFromBcl]`)
- [ ] Packages externos podem definir seus próprios mapeamentos
- [ ] BclMapper lê dos atributos em vez de hardcoded
- [ ] Mapeamentos default no `@meta-sharp/runtime` (assembly C# companion)

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
- [ ] Warnings para constructs C# não suportados (em vez de silent fail)
- [ ] `--dry-run` para preview do output
- [ ] `--verbose` para log detalhado da transformação

### @meta-sharp/runtime

- [x] `HashCode` (xxHash32)
- [x] Runtime type checks: `isChar`, `isString`, `isByte`, `isSByte`, `isInt16`, `isUInt16`, `isInt32`, `isUInt32`,
  `isInt64`, `isUInt64`, `isFloat32`, `isFloat64`, `isBool`, `isBigInt`
- [ ] `Decimal` wrapper (ou integração com decimal.js)
- [ ] `equals()` / `hashCode()` utilities para comparação deep
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

### Outros Targets

- [ ] Gerar Dart (para Flutter) — mesma AST intermediária, Printer diferente
- [ ] Gerar Kotlin — para uso Android nativo

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
