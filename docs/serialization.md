# JSON Serialization

Metano transpiles `System.Text.Json.Serialization.JsonSerializerContext`
subclasses into a TypeScript `SerializerContext` with **pre-computed `TypeSpec`
definitions**. JSON property names, naming policies, and per-property overrides
are all resolved at **compile time** — no runtime reflection, no string
comparisons.

## The triangle

There are three representations of your data, and each transition has its own
rules:

```mermaid
flowchart TD
    CS[C# source]
    TS[TypeScript target]
    JSON[JSON wire format]

    CS -->|transpile| TS
    TS -->|stringify| JSON
    JSON -->|parse| TS
```

Naming conventions differ at each layer:

| Layer | Default naming | Example |
|-------|---------------|---------|
| C# property | PascalCase | `FirstName` |
| JSON wire | depends on `JsonSourceGenerationOptions` (`PropertyNamingPolicy`, or camelCase under `JsonSerializerDefaults.Web`) | `firstName`, `first_name`, `FirstName` |
| TypeScript field | camelCase (Metano convention) | `firstName` |

The TypeScript `toJSON()` and `fromJSON()` maps **TS field ↔ JSON wire** — C#
is the source of truth for deriving both, but they don't know about each other.

## Minimal example

**Input** — `JsonContext.cs`:

```csharp
using System.Text.Json.Serialization;
using Metano.Annotations;

namespace TestApp;

[Transpile]
[StringEnum]
public enum Priority
{
    Low,
    Medium,
    High,
}

[Transpile]
public record TodoItem(string Title, bool Completed, Priority Priority);

[Transpile]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TodoItem))]
public partial class JsonContext : JsonSerializerContext;
```

Every type the context describes must be transpiled (`[Transpile]`, or
`[assembly: TranspileAssembly]` as in the rest of this page's examples): a root
that is not is error `MS0035` (`'TestApp.TodoItem' cannot be a
[JsonSerializable] root: it is not a transpiled class or struct of this project
or of a referenced Metano package, so it has no TypeScript contract.`).

**Output** — `json-context.ts`:

```typescript
import { SerializerContext, type TypeSpec } from "metano-runtime/system/json";
import { Priority } from "./priority";
import { TodoItem } from "./todo-item";

export class JsonContext extends SerializerContext {
  private static readonly _default: JsonContext = new JsonContext();

  private _todoItem?: TypeSpec<TodoItem>;

  static get default(): JsonContext {
    return this._default;
  }

  get todoItem(): TypeSpec<TodoItem> {
    return this._todoItem ??= this.createSpec({
      type: TodoItem,
      factory: (p: Record<string, unknown>) => new TodoItem(p.title as string, p.completed as boolean, p.priority as Priority),
      properties: [
        { ts: "title", json: "title", type: { kind: "primitive" } },
        { ts: "completed", json: "completed", type: { kind: "primitive" } },
        { ts: "priority", json: "priority", type: { kind: "enum", values: Priority } },
      ],
    });
  }
}
```

(The printer writes each property object on several lines; they are folded
here and in the other snippets of this page.)

**Usage** from TS:

```typescript
import { JsonSerializer } from "metano-runtime/system/json";
import { JsonContext } from "./json-context";
import { Priority } from "./priority";
import { TodoItem } from "./todo-item";

const todo = new TodoItem("Write docs", false, Priority.High);

// TS → JSON
const wire = JsonSerializer.serialize(todo, JsonContext.default.todoItem);
// → { title: "Write docs", completed: false, priority: "High" }

// JSON → TS (TodoItem | null: JSON `null` reads as `null`, as in STJ)
const parsed = JsonSerializer.deserialize(wire, JsonContext.default.todoItem);
// → TodoItem { title: "Write docs", completed: false, priority: "High" }

// JSON text → TS, as STJ's Deserialize(string): decimals keep every digit
const fromText = JsonSerializer.deserializeText(
  JSON.stringify(wire),
  JsonContext.default.todoItem,
);
```

`deserialize` takes the tree `JSON.parse` built; `deserializeText` takes the
JSON text, reads each `decimal` from its exact digits and throws .NET's
`JsonException` messages for invalid text ([details](#exact-reads)). Both
return `TodoItem | null` ([Null roots](#null-roots)).

## What the transpiler does

When it sees a class that inherits from `JsonSerializerContext`:

1. **Reads `[JsonSourceGenerationOptions]`** — resolves the naming policy
   (`CamelCase`, `SnakeCaseLower`, `SnakeCaseUpper`, `KebabCaseLower`,
   `KebabCaseUpper`, or `null`/default for PascalCase preservation). The
   `JsonSerializerDefaults.Web` constructor argument selects `CamelCase`, and a
   named `PropertyNamingPolicy` overrides it (see [Web defaults](#web-defaults)).

2. **Collects `[JsonSerializable(typeof(T))]` attributes** — each `T` becomes a
   lazy getter on the context class (see [Roots](#roots) for the accepted
   types, the getter names and the diagnostics).

3. **Adds every object type reachable from those roots**, as System.Text.Json
   does: types reached through property types, array / list / set elements,
   dictionary keys and values, nullable underlying types, and base types each
   get their own getter, so every `ref` descriptor and `base` link resolves.
   Roots come first, in declaration order, followed by the discovered types in
   breadth-first order; each type is emitted once, and cycles (`A → B → A`)
   terminate. The element, dictionary value and nullable inner types of a
   collection root are discovered the same way, so `typeof(ShotTiming[])`
   also emits `shotTiming`. Classes and structs are discovered when they are
   transpiled in their declaring assembly: this project (nested ones
   included) or a referenced `[TranspileAssembly]` + `[EmitPackage]` library,
   whose types the context imports from the package (and adds to
   `package.json#dependencies`). Closed generic types are discovered under
   their constructed name (`Page<EpisodeSummary>` → `pageEpisodeSummary`, with
   `TypeSpec<Page<EpisodeSummary>>`). Interfaces are not discovered. Base
   types, including a base declared in a referenced library, are included
   whenever the spec links to them through `base`. Every type registered with
   `[JsonDerivedType]` by a polymorphic base the context dispatches through (a
   root, a property / element / value type, or a registered type) is added
   too, in attribute order, with its own base chain and property types, even
   when no root names it (see [Polymorphism](#polymorphism)).

4. **For each serializable type `T`**, walks the members System.Text.Json's
   source generator serializes (see [Member selection](#member-selection)) and:
   - Excludes `[JsonIgnore]` (unless `Condition = Never`), `[JsonExtensionData]`
     and static members
   - Resolves the JSON name: `[JsonPropertyName]` override wins, otherwise
     applies the context's naming policy to the C# member name
   - Classifies the member type (primitive, temporal, decimal, bytes, array,
     map, branded, enum, ref, etc.)
   - Records a non-zero `[JsonPropertyOrder]` as the property's `order`

5. **Emits a `TypeSpec<T>`** as a lazy getter on the `SerializerContext`
   subclass, with pre-computed `ts`/`json` names, for every object type, and a
   **`ValueSpec<T>`** for every other root.

## Runtime version

Metano versions the transpiler tools and `metano-runtime` together, and a
generated context needs the runtime of the same major version. A context
generated by Metano 3.0 uses runtime members that earlier runtimes lack
(polymorphism descriptors with `abstract` entries and `baseTest`, `readOptions`,
`valueType`, the `bytes` descriptor, `PropertySpec.order`, `assignIfPresent`,
`deserializeText`), so it needs **`metano-runtime` 3.0.0 or later**. For
reference, value roots (`ValueSpec`) first shipped in 2.8.0 and the runtime's
polymorphic dispatch in 2.10.0; `[JsonFactory]` needs no runtime change.

You rarely set this by hand: every generated package that imports
`metano-runtime` declares it as a `peerDependency` of `^<transpiler version>`,
unless your `package.json` already declares it. The 3.0 release is a major
version because of the breaking changes in [Migrating to 3.0](#migrating-to-30).

## Roots

`[JsonSerializable]` accepts the same root types as System.Text.Json:

| Root | Getter | Spec |
|------|--------|------|
| `typeof(EpisodeSummary)` (a transpiled class, record or struct) | `episodeSummary` | `TypeSpec<EpisodeSummary>` |
| `typeof(IReadOnlyList<EpisodeSummary>)` | `iReadOnlyListEpisodeSummary` | `ValueSpec<EpisodeSummary[] \| null>` |
| `typeof(EpisodeSummary[])` | `episodeSummaryArray` | `ValueSpec<EpisodeSummary[] \| null>` |
| `typeof(IEnumerable<Leaf>)` | `iEnumerableLeaf` | `ValueSpec<Iterable<Leaf> \| null>` |
| `typeof(Dictionary<string, EpisodeSummary>)` | `dictionaryStringEpisodeSummary` | `ValueSpec<Map<string, EpisodeSummary> \| null>` |
| `typeof(HashSet<string>)` | `hashSetString` | `ValueSpec<HashSet<string> \| null>` |
| `typeof(string)`, `typeof(int)`, `typeof(int?)` | `string`, `int32`, `nullableInt32` | `ValueSpec<string \| null>`, `ValueSpec<number>`, `ValueSpec<number \| null>` |
| `typeof(Guid)`, a `[StringEnum]`, a `[Branded]` type | `guid`, the type's name | `ValueSpec<UUID>`, `ValueSpec<Mood>`, `ValueSpec<EpisodeId>` |
| `typeof(List<string>), TypeInfoPropertyName = "Names"` | `names` | `ValueSpec<string[] \| null>` |

A `ValueSpec` holds the same descriptor the type gets as a property, so it goes
through the same converters (custom converters still match by `kind`):

```typescript
const json = JsonSerializer.serialize(shots, AppJsonContext.default.shotTimingArray);
const restored = JsonSerializer.deserialize(data, AppJsonContext.default.shotTimingArray); // ShotTiming[] | null
```

A reference-type root (a collection, dictionary, set or `string`) is typed
`T | null`, because JSON `null` reads back as `null`; a value-type root is not,
and carries `valueType: true` instead ([Null roots](#null-roots)).
`IEnumerable<T>` / `IReadOnlyCollection<T>` roots and properties serialize any
iterable (a `Set`, a generator, a LINQ pipe) as a JSON array.

**Names** follow System.Text.Json's `JsonTypeInfo` property names, camel-cased:
a non-generic type uses its TypeScript name (so `[Name]` applies); a closed
generic type is its name without arity followed by its type arguments' names
(`ListListInt32`); an array is its element's name + `Array`; `Nullable<T>` is
`Nullable` + `T`'s name; BCL types use their CLR names (`String`, `Int32`,
`Guid`, `DateTimeOffset`). `TypeInfoPropertyName` replaces the name of its type
everywhere in the context, including `ref` / `base` links and the names of
generic types that use it as an argument. Type arguments use the same rule, so
`[Name("User")] UserDto` makes `List<UserDto>` → `listUser` (System.Text.Json:
`ListUserDto`).

**Diagnostics:**

- `MS0035` (error) rejects a root the context cannot describe: an open generic,
  `object`, a non-collection interface without `[JsonDerivedType]`
  registrations, a type that is not a transpiled class
  or struct (`KeyValuePair<,>`, tuples, `[Ignore]` / `[External]` types, types
  of non-Metano assemblies, and the dictionary types that are not recognized as
  dictionaries yet: `SortedDictionary`, `ReadOnlyDictionary`, `FrozenDictionary`),
  an array of rank > 1, a dictionary with non-`string` keys (not supported
  yet), a collection whose element type has no spec in the context (an
  interface or a type that is not transpiled), or a closed generic whose type
  argument has no spec (`Page<System.Version>`).
- `MS0036` reports two types that map to one getter (a warning that drops an
  unreferenced root, an error when the losing type is referenced by another
  spec), a getter that would shadow a `SerializerContext` member (`options`,
  `converters`, `createSpec`, `createValueSpec`, `readOptions`, `resolve`,
  `_registry`, `constructor`), and a type listed twice with different `TypeInfoPropertyName`
  values (a warning; the first one is kept). Use `TypeInfoPropertyName` to
  resolve a collision.

**Divergences:** `Stack<T>` is written and read in array order (System.Text.Json
writes it top-first and reverses it on read); the `[Name]` naming above.

**Getter renames (#270):** every getter now comes from the naming rule above.
A type whose camel-cased name is a JavaScript reserved word keeps the bare
name, because getters sit in property position: `Delete` → `get delete()`
(previously `get delete_()`), `Default` → `get default()` (previously
`default_`; it does not clash with the static `JsonContext.default`). Update
call sites such as `ctx.delete_` to `ctx.delete`.

### Null roots

JSON `null` at a root reads and writes as System.Text.Json does (measured on
.NET 10 with a source-generated context):

| Root | `deserialize(null, …)` / `deserializeText("null", …)` | Return type |
|---|---|---|
| A class or record contract, polymorphic and interface bases, a `[PlainObject]` type | `null` | `T \| null` (`TypeSpec<T>`) |
| A collection, dictionary, set, `string`, `byte[]` | `null` | `T \| null` (`ValueSpec<T \| null>`) |
| `Nullable<T>` (`int?`, `SPoint?`) | `null` | `T \| null` (`ValueSpec<T \| null>`) |
| A `struct` / `record struct` contract | throws `The JSON value could not be converted to MyApp.SPoint.` | `T \| null` (`TypeSpec<T>`; the throw makes the `null` impossible) |
| Another value type (`int`, `bool`, `Guid`, `decimal`, an enum, a branded type, a temporal type) | throws `The JSON value could not be converted to <Type>.` | `T` (`ValueSpec<T>`) |

- The generator marks every C# value type except `Nullable<T>` with
  `valueType: true` and its CLR `typeName`, as .NET prints it
  (`System.Int32`, `MyApp.Level`, `MyApp.Outer+Inner`,
  ``MyApp.Pair`1[System.Int32]``): on the struct contract's `TypeSpec`
  (wherever the closure reaches it) and on a value root's `ValueSpec`. The
  message names that `typeName`; a hand-written spec without one names its
  constructor, or `Object` for a value spec.
- At the root the check runs before any converter, custom ones included.
  Below the root, JSON `null` for a struct contract throws the same error for
  a property (`{"P":null}` for `record Holder(SPoint P)`), a list or set
  element and a dictionary value, as in .NET; `SPoint?` still reads `null`.
- `serialize(value, spec)` through a `TypeSpec` returns the written object for
  a non-null `value`, and `null` for `null`. A struct contract shares that
  overload, so `serialize(null, structSpec)` also returns `null`, a value C#
  cannot express (a struct is never `null`). A value root converts `null`
  through its descriptor, which writes `null`.
- Not mirrored: a value-type custom converter. .NET passes the `null` token
  to a custom converter of a value type, which decides what it reads; Metano
  throws before any converter runs. Also, JSON `null` for an `int`, `bool`,
  enum, branded or temporal **property, element or dictionary value** still
  reads `null`, where .NET throws (`The JSON value could not be converted to
  MyApp.Holder.` for a constructor-bound property, `System.Int32` for an
  element): their descriptors carry no value-type flag.
- An empty or blank text has no JSON token: `deserializeText("", …)` throws
  `The input does not contain any JSON tokens. Expected the input to start with
  a valid JSON token, when isFinalBlock is true.` for every root, as STJ does.

## Supported type descriptors

The runtime (`metano-runtime/system/json`) interprets `TypeSpec` objects and
handles these kinds:

| Kind | TS type | On serialize | On deserialize |
|------|---------|-------------|----------------|
| `primitive` | string, number, boolean | passthrough | passthrough; a JSON string for a numeric `numberType` when the context reads numbers from strings ([details](#web-defaults)) |
| `temporal` | `Temporal.PlainDate`, etc. | `.toString()` | `Type.from(iso)` |
| `decimal` | `Decimal` | `JSON.rawJSON(value.toFixed())`: its exact digits as a JSON number ([details](#decimals)) | `new Decimal(…)` from the JSON number (its exact digits through `deserializeText`), or from the JSON string when the context reads numbers from strings, rounded as .NET rounds it; anything else, or a value outside the decimal range, throws `The JSON value could not be converted to System.Decimal.` |
| `bytes` | `number[]` (C# `byte[]`) | padded standard base64, one line (`[1,2,3]` → `"AQID"`) | base64 as System.Text.Json reads it: space, tab, CR and LF ignored; anything else invalid throws `The JSON value could not be converted to System.Byte[].` |
| `array` | `T[]` / `Iterable<T>` | `Array.from(iterable, serialize)` | `.map(deserialize)` |
| `map` | `Map<K,V>` | `Object.fromEntries(...)`, each key written as text ([below](#dictionary-keys)) | `new Map(Object.entries(...))`, each property name parsed into the key type ([below](#dictionary-keys)) |
| `hashSet` | `HashSet<T>` | `Array.from(set, serialize)` | `new HashSet(arr)` |
| `branded` | `[Branded]` / `[InlineWrapper]` type | passthrough (brand erases at runtime) | `Type.create(value)`, or the type's `[JsonFactory]` ([below](#validating-branded-types)) |
| `enum` | string enum | passthrough (already string) | validates against allowed values |
| `numericEnum` | numeric enum | passthrough | validates against allowed values |
| `nullable` | `T \| null` | `null` passes through, else recurse | same |
| `ref` | another spec | recursive `serialize` with target spec | recursive `deserialize` |

### Dictionary keys

A JSON property name is always a string, so the key descriptor of a
dictionary carries what the runtime needs to read it back into the key's C#
type, the way System.Text.Json does (measured on .NET 10), whatever the
context's read options (#479):

| Key type | Key descriptor | Written | Read |
|---|---|---|---|
| `string` | `{ kind: "primitive" }` | as is | as is |
| `int`, `long`, `short`, `byte`, `uint`, … | `{ kind: "primitive", numberType: "System.Int32" }` | `String(n)` | the [numbers-from-strings grammar](#web-defaults): `"+5"`, `"007"` and `"-0"` read for a signed type, digits only for an unsigned one; whitespace, `"1.0"`, `"1e2"`, `""` and out-of-range values throw |
| `float`, `double` | `numberType: "System.Single"` / `"System.Double"` | `String(n)` | the same grammar, `"NaN"` and `"Infinity"` included |
| `bool` | `{ kind: "primitive", typeName: "System.Boolean" }` | `True` / `False` | `true` / `false` in any case, no whitespace |
| numeric enum | `{ kind: "numericEnum", values: Level, numberType: "System.Int32", typeName: "MyApp.Level" }` (`numberType` is the underlying type) | the member name (`High`; an aliased value by its first member), or the number when no member has it | a member name in any case, a comma-separated list of names (OR-ed), or an integer with an optional sign, any value in the underlying type's range, member or not; surrounding whitespace ignored |
| `[StringEnum]` | `{ kind: "enum", values: Mood, typeName: "MyApp.Mood" }` | the value | a member value in any case; surrounding whitespace ignored |
| `Guid` | `{ kind: "branded", create: UUID.create, typeName: "System.Guid" }` | as is | the `D` format (`8-4-4-4-12`) in any case, read as lowercase; braces, the `N` format and whitespace throw |
| `decimal` | `{ kind: "decimal", type: Decimal }` | `toFixed()` | the decimal grammar ([Decimals](#decimals)) |
| `DateOnly` and the other temporal types | `{ kind: "temporal", parse: …, typeName: "System.DateOnly" }` | as its value is written | the descriptor's `parse` |
| a `[Branded]` type | `{ kind: "branded", create: … }` | as is | `create(name)`, unchanged |

A property name the key type rejects throws `JsonException` `The JSON value
could not be converted to System.Int32.` (the key's CLR name: `System.Guid`,
`MyApp.Level`), on the tree and the text paths alike. Two names that read as
the same key (`"1"` and `"01"`) keep the value of the one JS enumerates last,
which puts integer-like names first: `{"01":1,"1":2}` reads `1` (.NET keeps
the last in the text, `2`). A custom converter of the key's kind still takes
precedence.

A `long` / `ulong` key reads as a `number` while 64-bit integers are numbers
(spec 012 turns them into `bigint`; [64-bit numbers](#64-bit-numbers)). A
dictionary with a non-`string` key can be a property but not yet a
`[JsonSerializable]` root (`MS0035`, [Roots](#roots)).

## Validating branded types

A `[Branded]` type's descriptor calls `Type.create(value)`, which only casts: the validation you
wrote in C# (`From`) never runs on JSON input. Mark the validating method with `[JsonFactory]` and
the descriptor calls it instead:

From the conformance sample (`samples/SampleJsonContracts/Title.cs`):

```csharp
[Branded]
public readonly record struct Title(string Value)
{
    [JsonFactory]
    public static Title From(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("blank");
        }
        return new Title(trimmed);
    }
}

public sealed record EpisodeRec(string Id, Title Note);
```

```ts
get title(): ValueSpec<Title> {
  return (this._title ??= this.createValueSpec({
    descriptor: { kind: "branded", create: Title.from }, // without [JsonFactory]: Title.create
    valueType: true,
    typeName: "SampleJsonContracts.Title",
  }));
}

// in the spec of EpisodeRec
{ ts: "note", json: "Note", type: { kind: "branded", create: Title.from } },
```

`JsonSerializer.deserialize` then throws the factory's own error for `"   "` (the transpiled
`Title.from` throws `Error("blank")`) and returns `"ok"` for `"  ok "`. Exceptions propagate unchanged, at any nesting depth (nullable, array, map value and map
key positions, and branded value roots). The member name is the one the companion declares, so
`[Name("parse")]` gives `create: Title.parse`, and a nested type gives `create: Episode.Code.from`.

A valid factory is a named static method (any accessibility) with exactly one parameter of the
wrapper's underlying type, returning the containing type; it is neither generic nor overloaded, is
emitted for TypeScript, is the type's only `[JsonFactory]`, and its TypeScript name is not `create`
or `toString` nor used by another method of the type. Anything else, or `[JsonFactory]` on a non-branded type, is error `MS0034` and the
descriptor keeps `create`. Without the attribute nothing changes.

In a library that declares `[EmitPackage]` the factory must be `public`: consumers compile against
the library's metadata, which omits non-public members, so a private factory would be dropped
there without notice and is reported as `MS0034` instead. Changing a library's factory without a
version bump can leave a consumer's cached context stale (the per-group cache does not track
referenced packages).

This is TypeScript-only: Metano ships no `System.Text.Json` converter that calls the factory, so
on the C# side `System.Text.Json` still writes the record struct as an object unless you supply
your own `JsonConverter`.

## Attribute support

| C# attribute | Handled by Metano? | Effect |
|---|---|---|
| `[JsonSerializable(typeof(T))]` | ✅ | Adds `T` and every object type reachable from it to the context's type list; collection, dictionary, set, primitive, enum, branded and nullable roots get a `ValueSpec` |
| `[JsonSerializable(..., TypeInfoPropertyName = "X")]` | ✅ | Names the getter `x` for that type everywhere in the context |
| `[JsonFactory]` (Metano) | ✅ | On a `[Branded]` type's static method: deserialization calls it instead of `create` ([details](#validating-branded-types)) |
| `[JsonSourceGenerationOptions]` with `PropertyNamingPolicy` | ✅ | Applies the naming policy to all properties |
| `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]` | ✅ | camelCase names, case-insensitive property names and numbers read from strings, as in .NET ([details](#web-defaults)) |
| `[JsonSourceGenerationOptions]` with `PropertyNameCaseInsensitive`, `NumberHandling = AllowReadingFromString` or `AllowOutOfOrderMetadataProperties` | ✅ | Mirrored in the context's `readOptions` ([details](#web-defaults)); the other `NumberHandling` flags (`WriteAsString`, `AllowNamedFloatingPointLiterals`) are not |
| `[JsonPropertyName("wire_name")]` | ✅ | Overrides the wire name for that property |
| `[JsonIgnore]` | ✅ | Excludes the property from the spec; on an override, also hides the base declarations ([details](#inheritance)). `Condition = Never` keeps it; `WhenWritingNull`, `WhenWritingDefault`, `WhenWriting` and `WhenReading` keep it too, always written and read, and warn `MS0037` |
| `[JsonInclude]` | ✅ | Serializes a field or an `internal` / `protected internal` property ([details](#member-selection)); on a member the source generator cannot reach, warns `MS0037` |
| `[JsonPropertyOrder(n)]` | ✅ | Emitted as the property's `order`; the runtime sorts the whole hierarchy by it ([details](#member-selection)) |
| `JsonKnownNamingPolicy.CamelCase` | ✅ | `FirstName` → `firstName` |
| `JsonKnownNamingPolicy.SnakeCaseLower` | ✅ | `FirstName` → `first_name` |
| `JsonKnownNamingPolicy.SnakeCaseUpper` | ✅ | `FirstName` → `FIRST_NAME` |
| `JsonKnownNamingPolicy.KebabCaseLower` | ✅ | `FirstName` → `first-name` |
| `JsonKnownNamingPolicy.KebabCaseUpper` | ✅ | `FirstName` → `FIRST-NAME` |
| Default (`null`, `JsonKnownNamingPolicy.Unspecified`) | ✅ | Preserves PascalCase (`FirstName` → `FirstName`) |
| `[JsonConverter]` | ⚠️ | Not mirrored, `MS0037` — use custom converters at runtime instead. Not reported on a `[Branded]` / `[InlineWrapper]` type, whose descriptor already writes the wrapped value |
| `[JsonRequired]`, C# `required` | ✅ / ⚠️ | A non-nullable member is already required on read; on a nullable member a missing key reads `null` where STJ throws, `MS0037` |
| `[JsonExtensionData]` | ⚠️ | Left out of the spec, `MS0037`; bound to a constructor parameter (STJ throws `InvalidOperationException`), the factory passes `null` |
| `[JsonNumberHandling]`, `[JsonObjectCreationHandling(Populate)]`, `[JsonUnmappedMemberHandling(Disallow)]` | ⚠️ | Not mirrored, `MS0037` |
| `[JsonConstructor]` | ✅ / ⚠️ | A class with two or more constructors is built through the factory of the marked overload (`factory: (p) => Money.fromAmountCurrency(…)`, spec 013 FR-035). Any other type is built through the constructor the factory calls (the primary one, else the one with the most parameters); the attribute on another constructor (a `[PlainObject]` secondary one, a type read from a DLL or package) reports `MS0037` |
| Any other `[JsonSourceGenerationOptions]` option | ⚠️ | `MS0037` when set to a value other than System.Text.Json's default; formatting options (`WriteIndented`, `IndentCharacter`, `IndentSize`, `NewLine`) and `DefaultBufferSize` / `GenerationMode` are never reported ([details](#member-selection)) |
| `[JsonDerivedType]` / `[JsonPolymorphic]` on a class or record | ✅ | Emits the base's `polymorphism` descriptor and a spec for every registered type ([details](#polymorphism)) |
| `[JsonDerivedType]` on an interface or a `[PlainObject]` type | ✅ | Interface bases dispatch class variants by constructor; `[PlainObject]` variants are selected by synthesized guards ([details](#interface-and-plainobject-hierarchies)) |

## Member selection

The context serializes the members System.Text.Json's source generator
serializes (measured on .NET 10, spec 009 FR-046, DECIDED-25):

- Public instance properties, as before.
- With `[JsonInclude]`, a field or a non-public property, when the member and
  its accessors are `public`, `internal` or `protected internal`. A `private` or
  `protected` `[JsonInclude]` member (or a public property with a `private`
  setter) makes the System.Text.Json contract throw `InvalidOperationException`
  (SYSLIB1038); the transpiler warns `MS0037` and leaves a non-public one out.
  A public field without `[JsonInclude]` is not serialized (`IncludeFields`
  warns `MS0037`).
- Each type lists its properties, then its fields; the FR-009 hierarchy order
  applies across types ([Inheritance](#inheritance)).
- `[JsonPropertyOrder(n)]` sorts the properties of the whole hierarchy by `n`,
  stably (absent counts as `0`); the attribute of the most-derived declaration
  counts, so an override without it has order `0`:

```csharp
public class OrdBase
{
    public int Base1 { get; set; } = 1;

    [JsonPropertyOrder(5)]
    public int Base2 { get; set; } = 2;

    [JsonPropertyOrder(-5)]
    public int Base3 { get; set; } = 3;
}

public class OrdDer : OrdBase
{
    public int Der1 { get; set; } = 10;

    [JsonPropertyOrder(-5)]
    public int Der2 { get; set; } = 20;

    [JsonPropertyOrder(1)]
    public int Der3 { get; set; } = 30;
}

// new OrdDer() → {"Der2":20,"Base3":3,"Der1":10,"Base1":1,"Der3":30,"Base2":2}
```

```typescript
get ordDer(): TypeSpec<OrdDer> {
  return this._ordDer ??= this.createSpec({
    type: OrdDer,
    base: this.ordBase,
    factory: (p: Record<string, unknown>) => new OrdDer(),
    properties: [
      { ts: "der1", json: "Der1", type: { kind: "primitive" } },
      { ts: "der2", json: "Der2", type: { kind: "primitive" }, order: -5 },
      { ts: "der3", json: "Der3", type: { kind: "primitive" }, order: 1 },
    ],
    assignIfPresent: ["base1", "base2", "base3", "der1", "der2", "der3"],
  });
}
```

The runtime writes `new OrdDer()` through this spec as
`{"Der2":20,"Base3":3,"Der1":10,"Base1":1,"Der3":30,"Base2":2}`, the text .NET
writes.

On read, the factory passes the constructor's parameters. Members no parameter
binds are set as System.Text.Json sets them: an `init` or `required` member
always, through `Object.assign(new T(…), { … })` (a missing nullable one reads
`null`; a missing non-nullable one throws, where STJ sets `0` / the default),
and a member with a `set` accessor or a writable field that the contract's own
constructor does not bind (inherited ones included) only when the payload
carries its key (the spec's `assignIfPresent: [...]` list; the runtime assigns
it after construction, in wire order where STJ follows the payload's order),
so a missing key keeps the constructed value. One FR-045 difference remains on
both paths: a missing non-nullable key throws in the runtime, where STJ uses the
default (an `init` value type, `0`) or keeps the initializer (a non-nullable
`set` member of a `[PlainObject]` type, whose literal has no initializer to keep):

```csharp
public class Init
{
    public string? Note { get; set; } = "init";
    public int Count { get; set; } = 5;
}

// {"Count":1} → Note = "init", Count = 1 (STJ and TypeScript alike)

public sealed record InitRec(int A)
{
    public string? Note { get; init; }
}
```

```typescript
get init(): TypeSpec<Init> {
  return this._init ??= this.createSpec({
    type: Init,
    factory: (p: Record<string, unknown>) => new Init(),
    properties: [
      { ts: "note", json: "Note", type: { kind: "nullable", inner: { kind: "primitive" } }, optional: true },
      { ts: "count", json: "Count", type: { kind: "primitive" } },
    ],
    assignIfPresent: ["note", "count"],
  });
}

get initRec(): TypeSpec<InitRec> {
  return this._initRec ??= this.createSpec({
    type: InitRec,
    factory: (p: Record<string, unknown>) => Object.assign(new InitRec(p.a as number), { note: p.note as string | null }),
    properties: [
      { ts: "a", json: "A", type: { kind: "primitive" } },
      { ts: "note", json: "Note", type: { kind: "nullable", inner: { kind: "primitive" } }, optional: true },
    ],
  });
}
```

`{}` reads as `Init { note: "init", count: 5 }`; for `InitRec` it throws
`Missing required property: "A"`.

A get-only property is written and still required on read (see
[Errors and limits](#errors-and-limits)). A public property with a non-public
getter is not serialized, and an `internal` `[JsonInclude]` member of another
assembly is reachable only when that assembly grants the context's
`InternalsVisibleTo`.

A `byte[]` is written as base64 (`[255,254,253]` → `"//79"`) and read back with
System.Text.Json's rules; `List<byte>` stays a number array. From the
conformance sample (`samples/SampleJsonContracts`, manifest row M1):

```csharp
public sealed record Blob(byte[] Data, byte[]? Maybe, List<byte> Numbers);

// new Blob([1, 2, 3], [255, 254], [1, 2]) → {"Data":"AQID","Maybe":"//4=","Numbers":[1,2]}
```

```typescript
properties: [
  { ts: "data", json: "Data", type: { kind: "bytes" } },
  { ts: "maybe", json: "Maybe", type: { kind: "nullable", inner: { kind: "bytes" } }, optional: true },
  { ts: "numbers", json: "Numbers", type: { kind: "array", element: { kind: "primitive" } } },
],
```
 A `null` `byte[]`
property, `List<byte[]>` element or `Dictionary<string, byte[]>` value is written
`null`, where a source-generated context's fast path writes `""` (which it then
reads back as an empty array).

Everything else System.Text.Json offers is either mirrored or reported with
warning `MS0037` (`UnmirroredJsonOption`) at the option or attribute, so no
option or attribute is ignored silently (the remaining differences are the
documented divergences of spec 009): the unmirrored `[JsonSourceGenerationOptions]`
options set to a non-default value (`DefaultIgnoreCondition`,
`IgnoreReadOnlyProperties`, `IgnoreReadOnlyFields`, `IncludeFields`,
`UseStringEnumConverter`, `Converters`, `ReferenceHandler`,
`DictionaryKeyPolicy`, `ReadCommentHandling`, `AllowTrailingCommas`,
`AllowDuplicateProperties`, `UnmappedMemberHandling`, `UnknownTypeHandling`,
`PreferredObjectCreationHandling`, `RespectNullableAnnotations`,
`RespectRequiredConstructorParameters`, `MaxDepth` other than 64, and each
`NumberHandling` flag but `AllowReadingFromString`) and the attributes in the
table above. The full list is in
[the diagnostics contract](../specs/009-polymorphic-json/contracts/diagnostics.md).

## Web defaults

`[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]` sets three options
in .NET: the camelCase naming policy, case-insensitive property names and
numbers read from strings. Metano mirrors all three: names are computed at
transpile time, and the two read options are emitted once on the context.

```csharp
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Clip))]
[JsonSerializable(typeof(Stats))]
public partial class WebJsonContext : JsonSerializerContext;

public sealed record Stats(int Count, double Ratio);
```

```ts
export class WebJsonContext extends SerializerContext {
  protected override get readOptions(): JsonReadOptions {
    return { propertyNameCaseInsensitive: true, numberHandling: "allowReadingFromString" };
  }

  get stats(): TypeSpec<Stats> {
    return this._stats ??= this.createSpec({
      type: Stats,
      factory: (p: Record<string, unknown>) => new Stats(p.count as number, p.ratio as number),
      properties: [
        { ts: "count", json: "count", type: { kind: "primitive", numberType: "System.Int32" } },
        { ts: "ratio", json: "ratio", type: { kind: "primitive", numberType: "System.Double" } },
      ],
    });
  }
}
```

- Property names are camel-cased at transpile time by the same rule .NET uses:
  `URL` → `url`, `IOStream` → `ioStream`, `ABC_D` → `abC_D`, `Item_Name` →
  `item_Name`; a name that does not start with an uppercase letter
  (`alreadyCamel`, `_Leading`) is kept.
- A named `PropertyNamingPolicy` overrides the Web default, as in .NET:
  `(JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)`
  writes `first_name`, and `PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified`
  turns the policy off (`FirstName`).
- `[JsonPropertyName]` still wins over any policy.
- The discriminator of a polymorphic type is never renamed: `$type` or the
  `TypeDiscriminatorPropertyName` you set (`"Kind"` stays `"Kind"`), and it is
  matched case-sensitively on read.

**Read options.** The context overrides `readOptions` only when an option
differs from .NET's default; `createSpec` and `createValueSpec` attach them to
every spec, so `JsonSerializer.deserialize(data, ctx.stats)` honors them too. A
named option overrides the Web default, as in .NET:
`(JsonSerializerDefaults.Web, PropertyNameCaseInsensitive = false)` reads names
case-sensitively again, and `NumberHandling = JsonNumberHandling.Strict` stops
reading numbers from strings. `PropertyNameCaseInsensitive = true`,
`NumberHandling = JsonNumberHandling.AllowReadingFromString` and
`AllowOutOfOrderMetadataProperties = true` are mirrored on their own too.

- **Case-insensitive names.** JSON keys match property names the way .NET's
  `OrdinalIgnoreCase` does (`PATH` and `pAtH` read `Path`); keys apply in
  document order, so with two matching keys the last one wins
  (`{"path":"a","Path":"b"}` reads `"b"`). The discriminator stays
  case-sensitive (`{"Kind":"image"}` is a missing `kind` tag) and is never read
  as a property. `ß`, `ı` and `ſ` do not match `SS`, `I` and `S`, as in .NET;
  characters whose casing differs between the JS engine's Unicode version and
  .NET's may compare differently.
- **Numbers from strings.** Numeric properties, collection elements, dictionary
  values and roots accept a JSON string by .NET's grammar: signed integers take
  an optional sign and leading zeros (`"+5"`, `"007"`, `"-0"` → `0`); unsigned
  integers take digits only (`"+5"` and `"-0"` are rejected for `byte`, `ushort`,
  `uint` and `ulong`); `float` / `double` take `"1e2"`, `"1."`, `".5"`, `"+1.5"`
  and the exact `"NaN"`, `"Infinity"`, `"-Infinity"`; `decimal` takes the same
  text without the named literals. Whitespace, hex, `""`, digit separators and
  out-of-range values are rejected with `JsonException` `The JSON value could
  not be converted to System.Int32.` (the CLR name of the type, or
  ``System.Nullable`1[System.Int32]`` for `int?`). Enums, the text `"null"` for a
  nullable number and integer type discriminators never read strings, as in
  .NET; dictionary keys are always text and read by the same grammar whatever
  the option ([Dictionary keys](#dictionary-keys)). Writing is unchanged: numbers are written as JSON
  numbers. A `decimal` read from a string is built from the exact text, so
  `"1.2345678901234567890123456789"` keeps its significant digits (trailing
  zeros are dropped: `"1.10"` reads as `1.1`), rounded to 28 decimal places as
  .NET rounds it (`"1e-29"` reads as `0`).
- **Out-of-order metadata.** `AllowOutOfOrderMetadataProperties = true` lets the
  type discriminator follow other properties ([details](#runtime-dispatch)).
- **Divergences.** For a property bound to a constructor parameter (a record's
  positional property), .NET names the declaring type in the error (`The JSON
  value could not be converted to TestApp.Stats.`); Metano names the numeric
  type. A `float` read from a string keeps double precision, as a `float` read
  from a JSON number already does. Two properties whose names differ only in
  case make .NET throw `InvalidOperationException` (`The JSON property name for
  'Twins.A' collides with another property.`) when a case-insensitive context
  is first used; Metano does not report it.

The snake and kebab policies follow .NET's word splitting too: `Item_Name` →
`item_name`, `Value2` → `value2`, `A1B` → `a1_b`, `MyURLValue` → `my_url_value`.

The runtime `PropertyNamingPolicy` (`metano-runtime/system/json`) applies the
same rules to a context built by hand, except for characters whose Unicode
category or casing differs between the JS engine and .NET 10 (engine-dependent,
e.g. U+0295 under Bun).

Two properties whose names map to the same JSON name make .NET throw
`InvalidOperationException` when the context is first used: `ID` and `Id` under
camelCase, or an explicit `[JsonPropertyName("name")]` next to a `Name` property
the policy renames to `name`. Metano does not report it yet and emits both.

**Where the policy comes from.** Metano reads only the
`[JsonSourceGenerationOptions]` attribute on the context class. Options given to
the context at runtime override that attribute in .NET and are invisible to
Metano: options passed to the context's constructor
(`new WebJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web))`), or
set through ASP.NET Core's `JsonOptions` / `TypeInfoResolverChain`. A context
with no attribute that is built with camelCase options writes `firstValue` in
.NET while Metano emits `FirstValue`. Put `JsonSerializerDefaults.Web` (or the
`PropertyNamingPolicy`) on the attribute so both sides agree.

## Custom converters

The runtime `SerializerContext` accepts custom converters in its options:

```typescript
import { SerializerContext, type JsonConverter } from "metano-runtime/system/json";
import { Decimal } from "decimal.js";

const decimalAsString: JsonConverter = {
  kind: "decimal",
  serialize: (v) => (v as Decimal).toString(),
  deserialize: (v) => new Decimal(v as string),
};

// If you construct your context manually:
class MyContext extends SerializerContext {
  constructor() {
    super({ converters: [decimalAsString] });
  }
}
```

## Decimals

The default `decimal` converter reads a JSON number (or, with
`AllowReadingFromString`, a JSON string) into a `Decimal`, and writes it with
its exact digits, as .NET does: `serialize` returns
`JSON.rawJSON(value.toFixed())`, which `JSON.stringify` prints unquoted and
without an exponent.

| C# value | .NET writes | Metano writes |
|----------|-------------|---------------|
| `19.99m`, `-42m` | `19.99`, `-42` | same |
| `decimal.MaxValue` | `79228162514264337593543950335` | same |
| `12345678901234567890.123456789m` | `12345678901234567890.123456789` | same |
| `0.0000001m` | `0.0000001` | same (decimal.js `toString()` would print `1e-7`) |
| `decimal.Negate(0m)` | `0` | same |
| `1.10m`, `1.0m`, `0.00m` | `1.10`, `1.0`, `0.00` | `1.1`, `1`, `0` |

The last row is the remaining gap: decimal.js drops trailing zeros (the scale),
so `1.10m` is written `1.1`. The value is the same; only the text differs.

Cost: writing a 1 MB `Order[]` with decimal.js prices (`bun run bench:json`)
takes 5.5–5.7 ms against 4.3 ms with `toNumber()` on Bun 1.4.2, which pays for
creating the `JSON.rawJSON` leaves, and the same 8.8–9.1 ms on Node 24.11.

Limits:

- **`deserialize` reads the tree `JSON.parse` built**, which has already
  rounded every JSON number to a double, so `12345678901234567890.123456789`
  reads as `12345678901234567000` there. Read the text with `deserializeText`
  to keep every digit ([Exact reads](#exact-reads)).
- **Engines without `JSON.rawJSON`.** The runtime detects `JSON.rawJSON` once
  when it loads (Node 24 and Bun 1.4.2 have it). Without it, decimals are
  written with `toNumber()`, as before 3.0: `7.922816251426434e+28` for
  `MaxValue`.
- **Values no `System.Decimal` holds** (NaN, ±Infinity, a magnitude beyond
  `79228162514264337593543950335`) throw `JsonException` `The value NaN cannot
  be written as System.Decimal: it is outside the System.Decimal range.`, on
  every engine. A value below `1e-29` is written `0`, which is what .NET reads
  for it (`1e-400000000` would otherwise print 400 million zeros).
- A value without `toFixed` is written with `toNumber()`. A hand-written
  descriptor without `type` keeps the raw JSON number or string it read and
  writes it back as is.

### Exact reads

`JsonSerializer.deserializeText(text, spec)` (and
`JsonSerializer.withContext(ctx).deserializeText(text, spec)`) reads JSON text
as .NET's `JsonSerializer.Deserialize(string, JsonTypeInfo<T>)` does:

```typescript
const order = JsonSerializer.deserializeText(
  '{"total":12345678901234567890.123456789}',
  JsonContext.default.order,
)!; // the text is not `null`
order.total.toFixed(); // "12345678901234567890.123456789"
```

- **Decimals** are built from their source digits and rounded as .NET rounds
  them: at most 28 decimal places and 96 bits, half to even (`2.5e-28` reads as
  `2e-28`, `1e-29` as `0`) except for a tie at the 30th digit of the text or
  later, which rounds up (leading fraction zeros count:
  `0.0000000000000000000000000000025e2` reads as `3e-28`), and a value that rounds past `MaxValue`
  (`79228162514264337593543950335.5`) throws `The JSON value could not be
  converted to System.Decimal.`. Every other value reads as `JSON.parse` reads
  it, custom converters included: a `double` property still gets a JS
  `number`.
- **64-bit integers** (3.0, [64-bit numbers](#64-bit-numbers)) are read from
  their exact token: digits beyond 2^53 arrive intact, and a number with a
  fraction or an exponent (`1.0`, `1e3`) throws, as .NET throws.
- **The path is chosen per spec.** A spec with no `decimal` and no 64-bit
  integer anywhere in its closure reads `JSON.parse(text)`, at the same cost as
  before. A spec with a `decimal` reads `JSON.parse(text)` too unless the text
  may hold a number of more than 15 digits, which only the runtime's tokenizer
  keeps exactly. On a 10 MB `Order[]` (`bun run bench:json`, Node 24.11 /
  Bun 1.4.2), prices of two decimal places read in 123.5 / 83.9 ms against
  114.7 / 78.1 ms through `JSON.parse` + `deserialize`; prices of 20 or more
  significant digits read exactly in 237.9 / 139.4 ms, against 124.0 / 84.1 ms
  for the inexact tree.
- **A 64-bit integer costs more.** A spec with a `long` / `ulong` anywhere in
  its closure takes the tokenizer whenever the text may hold a number with a
  fraction or an exponent — a digit followed by `.`, `e` or `E` anywhere,
  strings included (`"v1.0"`, a GUID's `"3e4f…"`) — so in practice for most
  payloads that also carry a `double`, a `decimal` or such strings. Only a
  text of integers and plain strings keeps `JSON.parse`. On a 5.8 MB array of
  `{ id: long, x, y, z, w: double }` (77 800 items, Node 24.11 / Bun 1.4.2),
  `deserializeText` takes 72–82 / 41–42 ms, against 37–38 / 22–23 ms for the
  same payload read through a spec whose `id` is not 64-bit (about 2x Node,
  1.9x Bun). Nearly all of the difference is the tokenizer itself, which is
  slower than the engine's `JSON.parse`; marking a short fraction (`1.5`) as
  non-integer costs one small object and no copy of its text.
- **Invalid text** throws `JsonException` with .NET's reader message (`'}' is
  an invalid start of a value.`, `Expected depth to be zero at the end of the
  JSON payload. …`), not the engine's `SyntaxError`. The runtime accepts what
  `JSON.parse` accepts, so lone surrogates in strings, which .NET rejects, are
  read. The text is read whole before the contract, so a syntax error is
  reported before a contract error that precedes it in the text.
- **Depth.** The limit of 64 counts the containers the contract reads, whichever
  path read the text ([Errors and limits](#errors-and-limits)). The tokenizer
  itself stops at 1 000 levels, short of the stack, where `JSON.parse` reads on.
  Which one reads the text depends on its numbers, so the same deeply nested
  payload can read or throw: a value deeper than 1 000 levels in an `object`
  slot (which the contract does not count) reads through `JSON.parse`, but
  throws `The JSON text nests containers deeper than 1000 levels…` once the
  text also holds `1.0` anywhere (even in a string) and the spec has a 64-bit
  integer — or a number of 16+ digits and the spec has a `decimal`. .NET
  rejects both at its default `MaxDepth` of 64; the two runtime paths are left
  apart (bounding `JSON.parse` would cost every read a walk) and documented
  here instead.

## 64-bit numbers

From spec 012 (3.0.0), every `long` / `ulong` (and `nint` / `nuint`) property,
element, dictionary value or root carries an `int64` descriptor instead of a
`primitive` one, whatever the read options — including a `[Branded]` type over
a 64-bit integer, whose descriptor keeps the branded companion factory and CLR
`typeName` and applies them after the exact read:

```typescript
{
  ts: "id",
  json: "id",
  type: {
    kind: "int64",
    numberType: "System.Int64",
    typeName: "MyApp.SharedKernel.SnowflakeId",
    create: SnowflakeId.from,
  },
}
```

**Native integers.** `nint` follows `long` and `nuint` follows `ulong` (spec
012 DECIDED-2): the same `int64` descriptor, `numberType` `System.Int64` /
`System.UInt64`, the same exact reads and writes, range checks, dictionary keys
and `[Branded]` / `[InlineWrapper]` wrappers. System.Text.Json itself has no
converter for `IntPtr` / `UIntPtr` (it throws `NotSupportedException` for a
property, a root or a dictionary key; measured on .NET 10 and 11), so a payload
for a native integer has no STJ counterpart: the contract is Metano's, and a
rejected value names `System.Int64` / `System.UInt64` rather than the CLR
`System.IntPtr`.

**Writes.** The converter writes a `bigint` through the shared exact-number
writer (`writeExactNumber`), so `JSON.stringify` prints the exact digits,
unquoted, byte-identical to System.Text.Json. A `rawJSON` leaf and a safe
integer write the same way; a JS `number` beyond `Number.MAX_SAFE_INTEGER`
throws `JsonException` (the digits are already gone), and so does a
non-integer `number`.

**Reads from a tree.** The converter accepts, in order: a `bigint` within the
type's range; a `rawJSON` leaf whose text is an integer token
(`1.0` / `1e3` are rejected, `-0` reads as `0n`); a safe integer `number`
(read as `BigInt(n)`); and — when the context reads numbers from strings — a
string in the [numbers-from-strings](#web-defaults) grammar, which now comes
back as a `bigint`. A `number` beyond the safe integers throws
`JsonException` naming `deserializeText`, because `JSON.parse` already lost
the digits (spec 012 OQ-3). Anything else throws
`The JSON value could not be converted to System.Int64.` (`` …
System.Nullable`1[System.Int64].`` for a nullable slot; `System.UInt64` for a
`ulong`).

**Reads from text.** A root whose closure contains a 64-bit member reads
exactly through `deserializeText` — the same forward-looking tokenizer the
decimals use — so ids above 2^53 arrive with every digit, in every position
(a property, a `long?`, an array element, a dictionary value, a `[Branded]`
id). The tokenizer hands the converter the source text of every number that
is not a short integer token, so the tree rules above apply to the token
itself: `1.0`, `1e3` and `-0.0` throw, as .NET throws, and `-0` reads as `0n`
(`ulong`: throws). A polymorphic int tag written `1.0` or `1e0` throws through
the same path, as .NET throws. The tree path is exact only for `bigint` and
`rawJSON` values; see the sample reads in
[`targets/js/sample-issue-tracker/test/json-int64.test.ts`](../targets/js/sample-issue-tracker/test/json-int64.test.ts).

**Dictionary keys.** A 64-bit dictionary key is written as its digits
(`"1152921504606846977"`), as System.Text.Json writes the property name, and
read back as a `bigint` key — never a rounded `number`. A `long`-keyed
dictionary can be a property but not a `[JsonSerializable]` root (`MS0035`);
wrap it in a record to put it on the wire.

**The `JSON.stringify` hazard.** A transpiled object holding a raw `bigint`
throws under plain `JSON.stringify` (`Do not know how to serialize a BigInt`);
the runtime does not patch `BigInt.prototype.toJSON` (no global side effects).
The generated context is the supported writer — see [When to use
`[PlainObject]`](#when-to-use-plainobject-vs-jsonserializercontext) for the
plain-object rule of thumb.

**Migration.** The 3.0 items — the type change, the tree leaf, the hazard,
the interop warnings (`MS0065`) and the runtime helpers that changed meaning
(`isInt64` / `isUInt64`, `tryParseInt64`, the `bigint` hash) — are
listed in [Migrating to 3.0](#migrating-to-30) and the spec's own migration
section.

## Migrating to 3.0

**Null roots (#268, FR-037).** `deserialize` and `deserializeText` through a
`TypeSpec` now return `T | null`: the JSON text `null` used to throw and now
returns `null`, as `System.Text.Json` does for reference types. Code such as
`const order: Order = JsonSerializer.deserialize(data, ctx.order);` no longer
compiles under `strict`. Handle the null, or assert it when the payload can
never be `null`:

```typescript
const order = JsonSerializer.deserialize(data, JsonContext.default.order);

if (order === null) {
  throw new Error("expected an order");
}

const sure = JsonSerializer.deserialize(data, JsonContext.default.order)!;
```

`serialize` through a `TypeSpec` now also accepts a value that may be `null`
(`Order | null`), returns `null` for it, and is then typed
`Record<string, unknown> | null`; a non-null value keeps the
`Record<string, unknown>` result. `BoundSerializer` changes the same way. Struct contracts (`record struct`) and
value-type roots still throw `JsonException` for `null` ([Null
roots](#null-roots)). Value roots (`ValueSpec`) already carry `| null` in
their type argument for a reference type.

**Decimals.** `JsonSerializer.serialize` used to return a `number` for each decimal; it now
returns a `JSON.rawJSON` leaf, a frozen object whose `rawJSON` property holds
the text. Only `JSON.stringify` treats it as a number: `structuredClone` and
`postMessage` throw `DataCloneError` on a tree that holds one, and `{ ...leaf }`
is the plain object `{"rawJSON":"2.5"}` (measured on Node 24 and Bun 1.4.2).
Stringify the tree before cloning or posting it.

- `JSON.stringify(JsonSerializer.serialize(...))` works unchanged and now
  writes every digit.
- Passing the tree straight back to `JsonSerializer.deserialize` works: the
  converter reads a leaf from its exact text.
- Code that inspects the tree and expects a `number` (`typeof json.price ===
  "number"`, arithmetic on it) must handle the leaf:

  ```typescript
  // TypeScript's lib does not declare JSON.rawJSON / JSON.isRawJSON yet.
  const { rawJSON } = json.price as { readonly rawJSON: string };
  const price = new Decimal(rawJSON); // or Number(rawJSON), which rounds
  ```

  or register a custom `decimal` converter that returns what the code expects.

**Wire format changes (2.10.1 → 3.0).** These change the JSON a 3.0 context
writes or accepts, so a payload exchanged with a 2.x build can differ:

- **`byte[]`** is written as base64, as .NET writes it (`[1,2,3]` →
  `"AQID"`). 2.x wrote a number array; a 2.x payload such as
  `{"Data":[1,2,3]}` now throws `The JSON value could not be converted to
  System.Byte[].` ([Member selection](#member-selection)).
- **Conditional `[JsonIgnore]`** (`WhenWritingNull`, `WhenWritingDefault`,
  `WhenWriting`, `WhenReading`): 2.x left these members out; 3.0 writes and
  reads them (warning `MS0037`), so a payload without a non-nullable one throws
  `Missing required property: "Count"`.
- **`JsonSerializerDefaults.Web`**: 2.x ignored it; 3.0 writes camelCase names
  (`FirstName` → `firstName`) and reads case-insensitively, numbers from
  strings included ([Web defaults](#web-defaults)).
- **`[JsonDerivedType]`** on an existing hierarchy: 2.x ignored it; 3.0 writes
  the tag (`{"$type":"cat","Name":"c"}`) and requires it only when reading
  through an abstract or interface base — a concrete base reads without one.
  A registration the context cannot honor fails the build with `MS0032`
  ([Polymorphism](#polymorphism)).
- **JSON `null` for a struct** property, list or set element, or dictionary
  value now throws `The JSON value could not be converted to MyApp.SPoint.`,
  as .NET does ([Null roots](#null-roots)).
- **Dictionary keys** are parsed into their key type
  ([Dictionary keys](#dictionary-keys), #479): a `Dictionary<int, …>` reads
  `Map<number, …>` keys (2.x kept the strings `"1"`), and a name the key type
  rejects (`"x"` for an `int`) now throws. Numeric enum keys are written as
  member names (`"High"`, 2.x wrote `"1"`) and `bool` keys as `True` /
  `False` (2.x: `true` / `false`), as .NET writes them; 2.x payloads still
  read.

**64-bit integers (spec 012).** `long` / `ulong` (and `nint` / `nuint`) are
`bigint` now, and the JSON tree changes shape around them
([64-bit numbers](#64-bit-numbers)):

- Generated types say `bigint` where 2.x said `number` (`ticks?: bigint`).
  Hand-written TS that passes `number`s fails `tsc` (TS2322 / TS2365): pass
  `5n` / `BigInt(n)`, convert with `Number(x)`.
- `serialize` returns `rawJSON` leaves for 64-bit values (as for decimals);
  `JSON.stringify` prints them unquoted. `structuredClone` of such a tree
  throws `DataCloneError`; stringify the tree first.
- Reads beyond 2^53 are exact only through the exact path: a tree the caller
  parsed with `JSON.parse` already lost the digits, and a `number` leaf beyond
  the safe integers throws pointing at `deserializeText`.
- A 64-bit dictionary key is written as its digits and read back as a
  `bigint` key (2.x kept the key strings, as for every numeric key).
- `JSON.stringify` on a transpiled object with a 64-bit field throws; the
  generated context is the writer (`c.json(dto)` in Hono needs
  `JSON.stringify(serializer.serialize(dto, spec))`).
- Interop declarations (`[Import]`, `[Emit]`, `[External]`, user
  `[MapMethod]` / `[MapProperty]` templates) exchange `bigint` with
  JavaScript now; the compiler warns `MS0065` at each one.
- Overflow wraps and `checked` throws like C# (`long.MaxValue + 1` is
  `long.MinValue`); `/` by zero throws `DivideByZeroException` for `long` too.
- Runtime helpers that keep their name and change meaning: `isInt64` /
  `isUInt64` test a `bigint` over the whole `long` / `ulong` range (2.x
  tested a safe-integer `number`), `tryParseInt64` returns `[found, bigint]`
  and takes an optional `NumberStyles` value (2.x returned a `number`), and
  every `bigint` hash — `HashCode.add`, `valueHashCode`, `hashInt64` — is
  the .NET word fold, so `BigInteger` hash values computed in 2.x no longer
  match.
- Overload dispatch decides `M(int)` vs `M(long)` by `typeof` now (2.x
  decided by range), and `object o = 5UL; o is long` is `true` where C# says
  `false` (a raw `bigint` reads its range; see
  [bcl-mappings](bcl-mappings.md#runtime-type-tests-and-dispatch)).
- `(int)someDouble` truncates now — floating-point casts were erased in 2.x
  (`(int)3.7` was `3.7`) and saturate like .NET today.

**Other changes.** Spec 009 changed these too; the first, second and fourth
already shipped in 2.x:

- Properties are written most-derived first, as .NET writes them
  ([Inheritance](#inheritance), 2.10.0); compare JSON semantically, not as
  text.
- The runtime's deserialization errors are `JsonException` (or
  `NotSupportedException` for polymorphism), with .NET's messages
  ([Errors and limits](#errors-and-limits), 2.10.0). Catch `Error` or the new
  classes instead of matching `Expected object for deserialization`.
- Text-only changes, read back the same: `TimeOnly` with a fraction is written
  with seven digits, as .NET writes it (`"03:04:05.678"` → `"03:04:05.6780000"`),
  and `[JsonPropertyOrder]` reorders the keys.
- Generic roots are named after System.Text.Json's `JsonTypeInfo` names
  (`ctx.list` → `ctx.listEpisodeSummary`; [Roots](#roots), 2.8.0); set
  `TypeInfoPropertyName` to choose another name.
- `deserializeText` reads decimals exactly, rounded to 28 decimal places
  ([Exact reads](#exact-reads)).
- The generated code needs `metano-runtime` 3.0.0 ([Runtime
  version](#runtime-version)).

## Inheritance

When a record extends another transpilable record (of this project or of a
referenced Metano package), the child's `TypeSpec` references the parent's
spec via a `base` field:

```csharp
public record Animal(string Name, int Legs);
public record Dog(string Name, int Legs, string Breed) : Animal(Name, Legs);

[JsonSerializable(typeof(Dog))]
public partial class InheritJsonContext : JsonSerializerContext;
```

```typescript
get dog(): TypeSpec<Dog> {
  return this._dog ??= this.createSpec({
    type: Dog,
    base: this.animal, // ← Name and Legs come from animal's spec
    factory: (p: Record<string, unknown>) => new Dog(p.name as string, p.legs as number, p.breed as string),
    properties: [
      { ts: "breed", json: "Breed", type: { kind: "primitive" } },
    ],
  });
}
```

The factory receives every constructor argument, inherited ones included. At
serialize/deserialize time, the runtime walks the `base` chain
(`collectWireProperties`) and writes properties in System.Text.Json's order:
the type's own properties first, then its base's, recursively
(`new Dog("Rex", 4, "lab")` → `{"Breed":"lab","Name":"Rex","Legs":4}`). A property redeclared in a derived type
(`override` or `new`) under the same JSON name is written once, at the
most-derived declaration's position and with its type; an override renamed
with `[JsonPropertyName]` is written under both names, as STJ does. When the
base emits no spec (`[Ignore]`, a non-transpiled base), its properties are
inlined into the derived spec in the same order. Reading does not depend on key
order. The exported `collectProperties` still lists properties base-first.

A `[JsonIgnore]` override hides every base declaration of that property, as in
System.Text.Json, which neither writes nor reads it. A linked spec records the
hidden members in `ignoredProperties` (TS names, e.g. `ignoredProperties:
["name"]`); the base spec keeps the property for its own instances. The
factory of the derived type then receives no value for it.

## Polymorphism

A class or record that carries at least one `[JsonDerivedType]` (with or
without `[JsonPolymorphic]`) is polymorphic, as in System.Text.Json. Only the
type's **own** attributes count (both are `Inherited = false`).

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ImageClip), "image")]
[JsonDerivedType(typeof(SilenceClip), "silence")]
public abstract record Clip(double Seconds);

public sealed record ImageClip(string Path, double Seconds) : Clip(Seconds);
public sealed record SilenceClip(double Seconds) : Clip(Seconds);
public sealed record Timeline(string Name, Clip Main, IReadOnlyList<Clip> Clips);

[JsonSerializable(typeof(Timeline))]
public partial class StudioJsonContext : JsonSerializerContext;
```

The generated context adds a getter for every registered type (here
`imageClip` and `silenceClip`, although no root names them) and ends the
base's spec with a data-only `polymorphism` member:

```typescript
get clip(): TypeSpec<Clip> {
  return this._clip ??= this.createSpec({
    type: Clip,
    factory: (p: Record<string, unknown>) => {
      throw new Error("Cannot deserialize abstract type 'Clip' directly; deserialize it through a polymorphic spec.");
    },
    properties: [{ ts: "seconds", json: "Seconds", type: { kind: "primitive" } }],
    polymorphism: {
      discriminator: "kind",            // verbatim, never renamed by a naming policy
      abstract: true,                   // the C# base is abstract
      derived: [
        { tag: "image", spec: () => this.imageClip },
        { tag: "silence", spec: () => this.silenceClip },
      ],
    },
  });
}
```

- **Tags.** A string tag prints as a string, an int tag as a number
  (`[JsonDerivedType(typeof(IntA), 1)]` → `{ tag: 1, … }` and
  `{"$type":1,"X":5}` on the wire); an untagged registration has no `tag`. A
  base that registers itself links to its own getter
  (`{ tag: "b", spec: () => this.sBase }`). The default discriminator is
  `$type`. When **no** registration has a tag, System.Text.Json reads no
  metadata at all: a concrete base reads its own contract (`$`-prefixed keys
  are plain data), and an abstract base throws `NotSupportedException`
  `Deserialization of interface or abstract types is not supported. Type
  'UAB'.`; writes still dispatch.
- **Options.** `unknownDerivedType: "fallBackToBaseType"` /
  `"fallBackToNearestAncestor"` and `ignoreUnrecognizedTags: true` are emitted
  only when the attribute sets them. Under `FallBackToNearestAncestor` an
  abstract registered type is allowed and marked `abstract: true` on its entry:
  it is a write target for its unregistered subtypes, and reading its tag
  throws `NotSupportedException` `Deserialization of interface or abstract
  types is not supported. Type 'AMid'.`, as System.Text.Json does (an abstract
  registered type that is itself polymorphic reports its own missing tag
  instead).
- **Closed generic bases.** `[JsonDerivedType(typeof(GDer), "g")]` on
  `GBase<T>` with `GDer : GBase<int>`: the attributes are read from
  `GBase<T>`, and every construction the context dispatches through gets its
  own polymorphic spec (`get gBaseInt32(): TypeSpec<GBase<number>>`), which
  `GDer`'s `base` link names.
- **Only where the context dispatches.** A base reached only through a
  derived type's `base` link (for example a root `typeof(GDer)` alone) gets a
  plain spec with no `polymorphism`, no registrations and no diagnostics:
  System.Text.Json never builds its polymorphism either.
- **Abstract factory.** The factory of an abstract polymorphic base stays a
  throwing factory, which a polymorphic read never calls; its message tells to
  read through the polymorphic spec. A non-polymorphic abstract type keeps
  the message "polymorphic JSON deserialization is not supported yet".
- **Diagnostics.** `MS0032` (error, at the attribute) rejects a registration
  that is generic, not assignable to the base (to each construction the
  context dispatches through, for a generic base), not transpiled in its
  declaring assembly, abstract without `FallBackToNearestAncestor`, or a
  repeated type or tag; the registration is left out of `derived`. It also
  rejects, leaving the base without `polymorphism`, a sealed base, a
  `[JsonPolymorphic]` without any `[JsonDerivedType]`, a reserved
  discriminator (`$id`, `$ref`, `$values`), and a JSON property of a
  contract written with a tag that is named like the discriminator
  (System.Text.Json throws) or integer-like (JavaScript would write it before
  the tag); System.Text.Json rejects all of them too. `MS0033` (warning) flags
  an untagged registration (it reads back as a concrete base, and cannot be
  read through an abstract one) and a registered type that declares its own
  `[JsonDerivedType]`s (not re-dispatched through the outer base); both behave
  exactly as in System.Text.Json.
- **Known gap.** A closed generic registration
  (`[JsonDerivedType(typeof(GenD<int>), "g")]`) is `MS0032`, although
  System.Text.Json accepts it.
- **Read options.** The context's `AllowOutOfOrderMetadataProperties` option is
  mirrored in its `readOptions` ([details](#web-defaults)).

With the `Clip` context above, `new ImageClip("a.png", 1.5)` through
`ctx.clip` writes `{"kind":"image","Path":"a.png","Seconds":1.5}`, and reading
that text through `ctx.clip` returns an `ImageClip`. A `Timeline` writes the tag
inside each `Clip`-typed property and element:
`{"Name":"t","Main":{"kind":"image","Path":"a","Seconds":1},"Clips":[{"kind":"image","Path":"b","Seconds":2}]}`.

### Unknown types, unrecognized tags and metadata order

These come from the conformance sample (`samples/SampleJsonContracts`); each
result is a manifest row that .NET and the TypeScript output both pass.

```csharp
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(UNearA), "a")]
[JsonDerivedType(typeof(UNearAbs), "abs")]
public abstract record UNear(int A);

public record UNearA(int A, int B) : UNear(A);
public abstract record UNearAbs(int A) : UNear(A);
public sealed record UNearB(int A, int B, int C) : UNearA(A, B); // not registered

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(UBaseA), "a")]
public abstract record UBaseH(int A);

public sealed record UBaseX(int A, int X) : UBaseH(A); // not registered

[JsonPolymorphic(TypeDiscriminatorPropertyName = "k", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(IgnDer), "d")]
public record IgnBase(int V);

[JsonSourceGenerationOptions(AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(Clip))]
public partial class OutOfOrderJsonContext : JsonSerializerContext;
```

```typescript
// in the spec of UNear
polymorphism: {
  discriminator: "$type",
  abstract: true,
  derived: [
    { tag: "a", spec: () => this.uNearA },
    { tag: "abs", spec: () => this.uNearAbs, abstract: true },
  ],
  unknownDerivedType: "fallBackToNearestAncestor",
},

// in the spec of IgnBase
polymorphism: {
  discriminator: "k",
  derived: [{ tag: "d", spec: () => this.ignDer }],
  ignoreUnrecognizedTags: true,
},

// in OutOfOrderJsonContext
protected override get readOptions(): JsonReadOptions {
  return { allowOutOfOrderMetadataProperties: true };
}
```

| Call | Result |
|---|---|
| write `new UNearB(1, 2, 3)` through `uNear` | `{"$type":"a","B":2,"A":1}`: the nearest registered ancestor, `UNearA`, and its contract |
| read `{"$type":"a","B":2,"A":1}` through `uNear` | a `UNearA` |
| read `{"$type":"abs","A":1}` through `uNear` | `NotSupportedException` `Deserialization of interface or abstract types is not supported. Type 'UNearAbs'.` |
| write `new UBaseX(1, 2)` through `uBaseH` | `{"A":1}`: the base contract, no tag |
| read `{"k":"zzz","V":1}` through `ignBase` | an `IgnBase`: the unknown tag is ignored |
| read `{"Path":"a","kind":"image","Seconds":1}` through `OutOfOrderJsonContext`'s `clip` | an `ImageClip`: the tag may follow other keys |

An **untagged** registration (`[JsonDerivedType(typeof(NA))]`) writes the
registered type's contract with no tag and reads back as the base, so it warns
`MS0033`, as does a registered type with its own `[JsonDerivedType]`s
(`NMid` under `NBase`: `{"t":"mid","B":2,"A":1}` is written, and an `NLeaf`
through `NBase` throws `Runtime type 'NLeaf' is not supported by polymorphic
type 'NBase'.`, as in .NET).

### Interface and `[PlainObject]` hierarchies

```csharp
[JsonDerivedType(typeof(Sq), "sq")]
[JsonDerivedType(typeof(Circle), "circle")]
public interface IShape { double Area { get; } }

public sealed record Sq(double Side) : IShape { public double Area => Side * Side; }
public sealed record Circle(double Radius) : IShape { public double Area => 3 * Radius * Radius; }

[PlainObject]
[JsonDerivedType(typeof(Point), "point")]
[JsonDerivedType(typeof(Point3), "point3")]
public abstract record Shape;

[PlainObject] public record Point(double X) : Shape;
[PlainObject] public sealed record Point3(double X, double Y, double Z) : Point(X);
```

```typescript
get iShape(): TypeSpec<IShape> {
  return this._iShape ??= this.createSpec({
    factory: (p: Record<string, unknown>) => {
      throw new Error("Cannot deserialize interface type 'IShape' directly; deserialize it through a polymorphic spec.");
    },
    properties: [{ ts: "area", json: "Area", type: { kind: "primitive" } }],
    polymorphism: {
      discriminator: "$type",
      abstract: true,
      typeName: "IShape",
      derived: [{ tag: "sq", spec: () => this.sq }, { tag: "circle", spec: () => this.circle }],
    },
  });
}

get shape(): TypeSpec<Shape> {
  return this._shape ??= this.createSpec({
    factory: (p: Record<string, unknown>) => {
      throw new Error("Cannot deserialize abstract type 'Shape' directly; deserialize it through a polymorphic spec.");
    },
    properties: [],
    polymorphism: {
      discriminator: "$type",
      abstract: true,
      typeName: "Shape",
      derived: [
        { tag: "point", spec: () => this.point, test: isPoint },
        { tag: "point3", spec: () => this.point3, test: isPoint3 },
      ],
      testOrder: [1, 0],
    },
  });
}

get point3(): TypeSpec<Point3> {
  return this._point3 ??= this.createSpec({
    base: this.point,
    factory: (p: Record<string, unknown>) => ({ x: p.x as number, y: p.y as number, z: p.z as number }),
    properties: [
      { ts: "y", json: "Y", type: { kind: "primitive" } },
      { ts: "z", json: "Z", type: { kind: "primitive" } },
    ],
  });
}
```

The synthesized guard in `point.ts`:

```typescript
export function isPoint(value: unknown): value is Point {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.x === "number";
}
```

- **Interface bases.** A C# interface with `[JsonDerivedType]` is an abstract
  base: its spec has no `type` (a TS interface has no constructor), a throwing
  factory, the interface's own and inherited properties (written under
  `FallBackToBaseType`, in System.Text.Json's order: the interface first, then
  the interfaces it extends, sorted topologically), and `typeName` for the
  messages. Class variants are dispatched by constructor and have no `base`
  link to the interface. `Sq(2)` through `IShape` writes
  `{"$type":"sq","Side":2,"Area":4}`, as System.Text.Json does. An interface
  registered as a derived type is `MS0032`; an interface without
  `[JsonDerivedType]` is still not a JSON contract (`MS0035` as a root).
- **Synthesized guards.** Every `[PlainObject]` type a `[JsonDerivedType]`
  registers, and every concrete `[PlainObject]` type that is itself
  polymorphic, gets an `isT` guard in its own file, built like a
  `[GenerateGuard]` guard (the predicate only, without `assertT`), so every
  registered `[PlainObject]` type exports an `isT` function from its module,
  whether or not a context uses it. The
  context imports it as a value (`import { isPoint, type Point } from
  "./point"`; `Shape.isCircle` for a nested type; from the package for a
  library variant, whose own build emits the guard). A guard checks a member
  whose type has no guard of its own for presence (`v.from != null`).
- **Write order.** Guards run only for plain objects (prototype
  `Object.prototype` or `null`); class instances never reach them. A variant
  whose guard requires a subset of another variant's members is tested after
  it: `Point` accepts every `Point3`, so `testOrder: [1, 0]` tests `Point3`
  first, and `{ x: 1, y: 2, z: 3 }` writes `{"$type":"point3","Y":2,"Z":3,"X":1}`.
  `testOrder` is omitted when it equals attribute order.
- **Concrete `[PlainObject]` base.** A concrete base that does not register
  itself carries its own guard as `baseTest`: a plain object no variant guard
  accepts is written as the base, with no tag, when `baseTest` accepts it
  (`{"Name":"a"}`, as System.Text.Json writes an `Animal`); otherwise it is an
  unregistered runtime type named `Object` (`NotSupportedException` `Runtime
  type 'Object' is not supported by polymorphic type 'Animal'.` under
  `failSerialization`). A self-registered base is an entry with its own `test`.
- **Reads** dispatch on the tag, as for classes; a `[PlainObject]` entry's
  factory builds an object literal with the inherited members too.
- **Diagnostics.** `MS0032` rejects two `[PlainObject]` variants whose guards
  accept each other's values (the same required members with compatible test
  kinds, or no required member at all), variants with no consistent test
  order, and a variant whose guard accepts every value of a concrete base
  written through `baseTest` (`Dog2(string Name, int? Bark)` under
  `Animal2(string Name)` would capture every `Animal2`), leaving the base
  without `polymorphism`. It also rejects a registered `[PlainObject]` type
  that gets no guard (`[Import]`, `[ExportedAsModule]`, `[NoContainer]`).
  Members of the same name with different kinds (`string` vs `number`), or
  different `[Discriminator]` literals, tell variants apart.
- **Divergences.** A plain object carries no runtime type: an unregistered C#
  subtype whose shape satisfies a registered variant's guard is written as
  that variant, and one whose shape passes the base's `baseTest` is written as
  the base (`Cat4` → `{"Name":"c"}`), where System.Text.Json throws
  `NotSupportedException`; the runtime type name in the error is `Object`.
- **Known gap.** A generic concrete `[PlainObject]` base gets no guard, so no
  `baseTest` (generic `[PlainObject]` contracts do not compile under `tsgo`
  yet anyway).

### Runtime dispatch

- **Write.** A class instance is matched by its constructor: a registered type
  (the base too, when it registers itself) writes its tag as the first key,
  then its contract (`{"kind":"image","Path":"a.png","Seconds":1.5}`); an
  unregistered base instance is written with no tag; any other runtime type
  follows `unknownDerivedType` — `failSerialization` (default) throws
  `NotSupportedException` `Runtime type 'X' is not supported by polymorphic
  type 'Clip'.`, `fallBackToBaseType` writes the base contract with no tag,
  `fallBackToNearestAncestor` writes the nearest registered ancestor class (or
  the base). A plain object has no runtime type: it is matched by the `test`
  guards of `[PlainObject]` variants, in `testOrder`. A derived contract
  reached this way is never dispatched again, even when it is polymorphic too.
- **Read.** The tag counts only under its exact name and only as the first key
  (anywhere with `readOptions: { allowOutOfOrderMetadataProperties: true }`,
  which a context declaring `AllowOutOfOrderMetadataProperties = true` emits). A
  string tag never matches a number tag (`1` ≠ `"1"`). An unknown tag throws
  `JsonException` `Read unrecognized type discriminator id 'video'.` unless
  `ignoreUnrecognizedTags` is set; a missing tag reads a concrete base and, on
  an abstract or interface base, throws `NotSupportedException` `The JSON
  payload for polymorphic interface or abstract type 'Clip' must specify a type
  discriminator.`; a tag that is neither a string nor a number throws
  `JsonException` `The '$id', '$ref' or '$type' metadata properties must be
  JSON strings. Current token type is 'Null'.`, and a number outside `Int32`
  throws `The JSON value could not be converted to Clip.`.
- **Misplaced metadata.** A later tag on a concrete base throws `JsonException`
  `The metadata property is either not supported by the type or is not the
  first property in the deserialized JSON object.` (on an abstract base it is a
  missing tag), and so do `$id` / `$ref` and a later `$values` (a leading
  `$values` has its own message). Any other key that starts with `$`
  throws `Properties that start with '$' are not allowed in types that support
  metadata. …`. These checks apply only to polymorphic specs. JS enumerates
  array-index keys (`"0"`) first, so an index key written before the tag goes
  unnoticed, where STJ rejects it.
- **Abstract registrations.** An entry marked `abstract: true` is a
  nearest-ancestor target on write; reading its tag throws the
  `NotSupportedException` above (`$id`, `$ref` or `$values` right after the
  tag throw the misplaced-metadata `JsonException` first).
- Dispatch applies at the root and through `ref`, `array`, `hashSet`, `map` and
  `nullable` descriptors, so `List<Clip>`, `Clip[]` roots and `Clip`-typed
  properties all write and read the tag. A custom converter registered for `kind: "ref"`
  replaces it, as before.

## Errors and limits

Deserialization errors are `JsonException` (exported from
`metano-runtime/system/json`, extends `Error`, `name === "JsonException"`).
Messages follow System.Text.Json without its `Path` / `LineNumber` suffix:

| Input | Message |
|---|---|
| A non-object JSON value (or an array) for an object contract | `The JSON value could not be converted to Episode.` (`Object` for a `[PlainObject]` contract, which has no constructor); JSON `null` reads as `null` instead ([Null roots](#null-roots)) |
| JSON `null` at a struct contract or a value-type root (`valueType: true`), or for a struct property, element or dictionary value | `The JSON value could not be converted to MyApp.SPoint.` ([Null roots](#null-roots)) |
| A missing non-nullable property | `Missing required property: "title"` (STJ uses the default value unless the member is `required`; a documented divergence). A nullable member declared `required` or `[JsonRequired]` reads `null` when missing, where STJ throws (both warn `MS0037`) |
| An invalid base64 text, or a non-string JSON value, for a `byte[]` | `The JSON value could not be converted to System.Byte[].` (STJ names the declaring type for a constructor-bound property) |
| A non-array JSON value for an array, list or set | `Expected array for deserialization, got object` (STJ names the CLR collection type) |
| A non-object JSON value (or an array) for a dictionary | `Expected object for deserialization, got array` |
| A numeric JSON string the grammar rejects, when the context reads numbers from strings | `The JSON value could not be converted to System.Int32.` (STJ names the declaring type for a constructor-bound property) |
| An unknown string enum value | `Invalid enum value: …` (STJ: `The JSON value could not be converted to Color.`) |
| An unknown numeric enum value | `Invalid numeric enum value: …` — a divergence: STJ reads any number (`7` reads as `(Color)7`) |
| More than 64 nested containers on read | `The maximum configured depth of 64 has been exceeded. Cannot read next JSON object.` (`… JSON array.` for an array) |
| More than 64 nested containers on write, or an object cycle | `A possible object cycle was detected. This can either be due to a cycle or if the object depth is larger than the maximum allowed depth of 64. Consider using ReferenceHandler.Preserve on JsonSerializerOptions to support cycles.` |

The depth limit is System.Text.Json's default `MaxDepth` of 64: 64 nested
objects, arrays and dictionaries are written and read, the 65th throws; scalars
never count. On read, only the containers the contract visits count; a deeply
nested value under a key the contract does not declare is skipped without being
counted, where the STJ reader would throw, also through `deserializeText`
([Exact reads](#exact-reads)).

Invalid JSON text passed to `deserializeText` throws `JsonException` with the
STJ reader's message (`'x' is an invalid start of a value.`); `deserialize`
takes an already parsed value, so `JSON.parse` throws its own `SyntaxError`
first.

On write, Metano is a hybrid of STJ's two modes. Its boundary is the one of a
source-generated context at its default (fast-path) settings, which also writes
a scalar inside the 64th container and throws `InvalidOperationException`
`CurrentDepth (64) is equal to or larger than the maximum allowed depth of
64. …` at the 65th. Its exception and message are the metadata / reflection
mode's `JsonException` above, which that mode already throws for a non-null
scalar written inside the 64th container. A custom converter that serializes a
separate root inside a call inherits the outer depth, where STJ starts that
root at depth 0.

Ignored properties: a constructor parameter bound to a `[JsonIgnore]` property
receives `undefined` (STJ passes `default(T)`), and a `[JsonIgnore] new`
(non-override) redeclaration does not hide the base declaration, which is then
written from the single TS member that holds the derived value, at the base's
position (STJ writes the base value at the redeclaration's position).

Constructor access: a class with a `TypeSpec` in the context is passed as
`type: T` and built by `factory: (p) => new T(...)`, both outside its class
body, so its constructor stays public even when C# declares it `private` or
`protected` (warning `MS0061`, baseline FR-049, #432). A class with two or more constructors is
built through the factory of its `[JsonConstructor]` overload instead (`Money.fromAmount(…)`,
spec 013 FR-035); that factory is emitted public, with `MS0061`, when its overload is not.
Other classes keep their constructor's
`private` / `protected` modifier. A context that claims a type from a referenced
package with no public constructor does not type-check, because that package
decided the modifier when it was transpiled; the transpiler reports `MS0062` at
the context. It cannot see a type the package kept public through `MS0061`,
which warns although the package emitted a public constructor. A project
reference loads as source and is otherwise judged exactly; a binary reference
(a DLL or a package) is read from metadata, which also cannot tell an abstract
class's C# 12 primary constructor from a declared `protected` one.

### Hand-written specs

The runtime compiles each spec once, on first use, into a cached plan. Specs
that the transpiler generates always meet its assumptions. A spec written by
hand must meet them too:

- Property converters are resolved when the spec is first used. A malformed
  descriptor fails then, even when the value being converted lacks that
  property.
- A `ref` descriptor's `spec()` thunk is called once and its result is kept.
  It must return the same spec every time, as the generated `??=` context
  getters do.
- A `ref` that points at a raw spec object instead of the context getter does
  not inherit the context's read options, because the context attaches them in
  `createSpec`. This behavior predates the plans.

## Divergences from System.Text.Json

The conformance sample (`samples/SampleJsonContracts`) runs one manifest of
cases through the real System.Text.Json source-generated contexts and through
the generated TypeScript; both sides must agree, row by row, except for the
differences below. The full list, with the measured .NET behavior and the
manifest row of each, is the
[Documented divergences table of spec 009](../specs/009-polymorphic-json/spec.md#documented-divergences).

**Lost to `JSON.parse`.** `deserialize` receives a tree, so lexical facts of
the text are gone:

- duplicate keys: the last value wins, including a duplicate discriminator
  (STJ throws) and an earlier invalid value of the same property
  (`{"price":1e30,"price":1}` reads `1`); in a case-insensitive context
  `{"count":1,"COUNT":2,"count":3}` reads `2` (STJ: `3`);
- an int tag written `1.0` or `1e0` is accepted (STJ throws), and so is a
  `long` written `1.0` (read as `1n`); `deserializeText` rejects both for a
  spec with a 64-bit integer in its closure ([64-bit numbers](#64-bit-numbers));
- an array-index key (`"0"`) written before the tag is not seen as out of
  order;
- a `decimal` just past `MaxValue`, or with more than 15 significant digits,
  is already a double: use `deserializeText` ([Exact reads](#exact-reads));
- lone surrogates in strings are accepted, and a syntax error is reported
  before a contract error earlier in the text.

**Written text.**

- `decimal` drops trailing zeros (`1.10m` → `1.1`), and engines without
  `JSON.rawJSON` write `toNumber()` ([Decimals](#decimals));
- `NaN` / `±Infinity` are written `null` (STJ throws without
  `AllowNamedFloatingPointLiterals`); `1E+21` and `-0` print as `1e+21` and `0`;
- `TimeSpan` (`PT1H2M3S`) and `DateTimeOffset` (`…+00:00[UTC]`) are written as
  Temporal strings, which .NET does not read, and vice versa;
- `[StringEnum]` values are strings, where .NET writes numbers without
  `JsonStringEnumConverter`;
- `Dictionary<int, …>` keys come out in JS order (integer-like keys first);
  `Stack<T>` is written in array order;
- `double` dictionary keys print as `String()` prints them (`1e+21`, `0` for
  `-0`; .NET: `1E+21`, `-0`), a `NaN` key is written (.NET throws), a
  `decimal` key drops trailing zeros (`1.10` → `1.1`), a `Guid` key keeps the
  case it holds, and a `[Flags]` combination of a numeric enum key is written
  as its number (.NET: `A, B`);
- a `null` `byte[]` is written `null` (the .NET source-generated fast path
  writes `""`);
- a `[JsonIgnore] new` redeclaration writes the derived value at the base's
  position.

**Reading.**

- A missing non-nullable property throws `Missing required property: "…"`;
  .NET uses the default value unless the member is `required`. A missing
  nullable `required` / `[JsonRequired]` member reads `null` (`MS0037`).
- JSON `null` for an `int`, `bool`, enum, branded or temporal property,
  element or dictionary value reads `null` (.NET throws); a value-type custom
  converter never sees `null` ([Null roots](#null-roots)).
- An unknown numeric enum value throws (`7` reads as `(Color)7` in .NET);
  unknown string enum values and numeric enums read from strings throw with
  Metano's own messages.
- Dictionary keys: a `[StringEnum]` key does not read its member's number
  (.NET reads `"1"`); enum keys use the TS member names, so a member renamed
  with `[Name]` or `[JsonStringEnumMemberName]` does not read the name .NET
  writes; two names that read as one key keep the one JS enumerates last
  ([Dictionary keys](#dictionary-keys)); a temporal key reads whatever its
  `parse` accepts (`Temporal.PlainDate.from` takes `2024-01-02T00:00`, .NET
  throws).
- `set` members are assigned after construction in wire order, not in the
  payload's order; a constructor parameter bound to an ignored property gets
  `undefined`.
- The depth limit counts only the containers the contract visits, and
  `deserializeText` stops at 1 000 nested levels ([Errors and
  limits](#errors-and-limits)).
- Two property names that differ only in case in a case-insensitive context
  are not reported (.NET throws on first use); case-insensitive matching uses
  the JS engine's Unicode tables.

**Polymorphism.**

- A plain object carries no runtime type: an unregistered C# subtype of a
  `[PlainObject]` hierarchy is written as the registered variant (or the
  concrete base) its shape matches, and the error names `Object`
  ([details](#interface-and-plainobject-hierarchies)).
- A closed generic registration (`[JsonDerivedType(typeof(GenD<int>), "g")]`)
  is `MS0032`; integer-like property names in a tagged contract are `MS0032`.
- Messages name types without their namespace (`'Clip'`, .NET:
  `'SampleJsonContracts.Clip'`).

**Messages and limits.** Messages carry no `Path` / `LineNumber` suffix; a
constructor-bound property's conversion error names the property's type
(`System.Int32`, `System.Byte[]`) where .NET names the declaring type; the
collection errors keep Metano's `Expected array for deserialization, got …`
text; the write depth limit raises the metadata mode's `JsonException` at the
fast path's boundary; a custom converter that serializes a separate root
inherits the outer depth.

**Options.** Options Metano does not mirror are reported with `MS0037`
([Member selection](#member-selection)); options given to the context at
runtime are invisible to the transpiler ([Web defaults](#web-defaults)).

## When to use `[PlainObject]` vs `JsonSerializerContext`

Both are valid ways to handle JSON data:

| `[PlainObject]` | `JsonSerializerContext` |
|---|---|
| Emits a TS `interface`, no class | Emits a full class with spec-driven serialization |
| Use `JSON.stringify`/`parse` directly | Use `JsonSerializer.serialize`/`deserialize` |
| No naming policy handling | Full naming policy + per-property overrides |
| No nested type conversion | Handles `Temporal`, `Decimal`, `Map`, `HashSet`, branded types, etc. |
| Fast path for simple DTOs | Correct handling of non-JSON-safe types |

**Rule of thumb:**

- **Simple DTO** with only primitives and arrays of primitives? → `[PlainObject]`
- **Rich domain type** with dates, decimals, branded IDs, maps? → `JsonSerializerContext`

**64-bit fields.** From spec 012 (3.0.0) a `long` / `ulong` field of a
`[PlainObject]` type is a TS `bigint` (`ticks?: bigint`, FR-060), and
`JSON.stringify` throws on such an object — JavaScript has no JSON form for a
`bigint` (`TypeError: Do not know how to serialize a BigInt`). The supported
writer is the generated `JsonSerializerContext`, whose `int64` descriptor
writes the exact digits; use `JSON.stringify(serializer.serialize(dto, spec))`
when a framework helper calls `JSON.stringify` for you. Keep such DTOs
`number`-only (use `int` / `double`) when they must survive plain
`JSON.stringify`.

## See also

- [Attribute Reference](attributes.md#plainobject--record--interface) — `[PlainObject]` details
- [BCL Type Mappings](bcl-mappings.md) — how types are classified
- [Spec 009](../specs/009-polymorphic-json/spec.md) — System.Text.Json-faithful JSON contracts: requirements, decisions and the documented divergences
- [Diagnostic catalog](../specs/001-project-baseline-evolution/baseline/diagnostic-catalog.md) — `MS0032`–`MS0037`
- [Feature support matrix](../specs/001-project-baseline-evolution/baseline/feature-support-matrix.md) — current support status of the serialization features
- [`samples/SampleJsonContracts`](../samples/SampleJsonContracts/README.md) — the conformance sample checked against System.Text.Json in CI
