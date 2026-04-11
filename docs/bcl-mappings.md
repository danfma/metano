# BCL Type Mappings

Metano maps many C# standard library types to idiomatic TypeScript equivalents.
This page lists every mapping the transpiler knows about out of the box.

## Primitives

| C# | TypeScript |
|----|----|
| `string` | `string` |
| `bool` | `boolean` |
| `int`, `long`, `short`, `byte`, `sbyte`, `uint`, `ulong`, `ushort`, `float`, `double` | `number` |
| `decimal` | `Decimal` (from [`decimal.js`](https://mikemcl.github.io/decimal.js/)) |
| `BigInteger` | `bigint` |
| `char` | `string` (length 1) |
| `Guid` | `UUID` (branded `string`, from `metano-runtime`) |
| `Uri` | `string` |
| `object` | `unknown` |
| `void` | `void` |
| `T?` / `Nullable<T>` | `T \| null` |

### `decimal` specifics

Because JavaScript numbers are IEEE-754 (and lose precision for many financial
calculations), `decimal` maps to the `Decimal` class from the `decimal.js` npm
package. Operators are lowered to method calls:

| C# | TypeScript |
|----|----|
| `a + b` | `a.plus(b)` |
| `a - b` | `a.minus(b)` |
| `a * b` | `a.times(b)` |
| `a / b` | `a.dividedBy(b)` |
| `a == b` | `a.equals(b)` |
| `a > b` | `a.greaterThan(b)` |
| `decimal.Zero` | `new Decimal(0)` |
| `decimal.Parse("1.5")` | `new Decimal("1.5")` |
| `(decimal)42` | `new Decimal(42)` |
| Decimal literal `1.5m` | `new Decimal("1.5")` |

### `Guid` specifics

`Guid` maps to `UUID` — a **branded primitive type** shipped by `metano-runtime`.
At runtime it's literally a `string`, so serialization and interop with ordinary
string APIs work without any wrapper, but the type system distinguishes "any
arbitrary string" from "a validated UUID".

```typescript
// metano-runtime
export type UUID = string & { readonly __brand: "UUID" };
export namespace UUID {
  export function create(value: string): UUID;
  export function newUuid(): UUID;
  export function newCompact(): UUID;
  export const empty: UUID;
  export function isUuid(value: unknown): value is UUID;
}
```

**Lowerings:**

| C# | TypeScript |
|----|----|
| `Guid.NewGuid()` | `UUID.newUuid()` |
| `Guid.NewGuid().ToString("N")` | `UUID.newUuid().replace(/-/g, "")` (compact form) |
| `Guid.Parse(s)` | `UUID.create(s)` |
| `Guid.Empty` | `UUID.empty` |
| `guid.ToString()` | `guid` (identity — already a string) |

**Why branded?** The same rationale as `[InlineWrapper]` on user-defined IDs:
you get compile-time type safety without runtime overhead. A random `string`
can't be passed where a `UUID` is expected, but a `UUID` is still serializable
as JSON, loggable, and indexable like any other string.

**Escape hatches:** cast a `string` to `UUID` with `UUID.create(str)` (or `str as UUID`
if you're sure about the shape), and cast back to plain `string` with `uuid as string`
(though you usually don't need to — `UUID` is already structurally assignable to
`string` for any read-only API).

## Date / time (Temporal API)

Metano uses the [Temporal API](https://tc39.es/proposal-temporal/docs/) via the
`@js-temporal/polyfill` package.

| C# | TypeScript |
|----|----|
| `DateTime` | `Temporal.PlainDateTime` |
| `DateTimeOffset` | `Temporal.ZonedDateTime` |
| `DateOnly` | `Temporal.PlainDate` |
| `TimeOnly` | `Temporal.PlainTime` |
| `TimeSpan` | `Temporal.Duration` |
| `DateTime.Now` / `DateTimeOffset.UtcNow` | `Temporal.Now.plainDateTimeISO()` / `Temporal.Now.zonedDateTimeISO()` |
| `DateOnly.DayNumber` | `dayNumber(date)` (runtime helper) |

The `@js-temporal/polyfill` dependency is added to your `package.json`
automatically whenever any Temporal mapping is used.

## Collections

### Lists and arrays

| C# | TypeScript |
|----|----|
| `T[]` | `T[]` |
| `List<T>` / `IList<T>` / `IReadOnlyList<T>` | `T[]` |
| `List<T>.Add(item)` | `list.push(item)` |
| `List<T>.Contains(item)` | `list.includes(item)` |
| `List<T>.IndexOf(item)` | `list.indexOf(item)` |
| `List<T>.Count` | `list.length` |
| `List<T>.Remove(item)` | `listRemove(list, item)` (runtime helper) |
| `List<T>.ToArray()` | `list.slice()` |

### Immutable collections

`ImmutableList<T>` and `ImmutableArray<T>` both map to plain `T[]` with **pure
helper functions** that return new arrays. This keeps serialization simple while
preserving immutability semantics.

| C# | TypeScript |
|----|----|
| `ImmutableList<T>` / `ImmutableArray<T>` | `T[]` |
| `list.Add(item)` | `ImmutableCollection.add(list, item)` |
| `list.AddRange(items)` | `ImmutableCollection.addRange(list, items)` |
| `list.Insert(i, item)` | `ImmutableCollection.insert(list, i, item)` |
| `list.Remove(item)` | `ImmutableCollection.remove(list, item)` |
| `list.RemoveAt(i)` | `ImmutableCollection.removeAt(list, i)` |
| `list.Clear()` | `[]` |

### Dictionaries

| C# | TypeScript |
|----|----|
| `Dictionary<K,V>` / `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` | `Map<K,V>` |
| `dict.Add(k, v)` | `map.set(k, v)` |
| `dict.ContainsKey(k)` | `map.has(k)` |
| `dict.Remove(k)` | `map.delete(k)` |
| `dict.Count` | `map.size` |
| `dict[key]` (read) | `map.get(key)` |
| `dict[key] = value` (write) | `map.set(key, value)` |
| `dict.TryGetValue(k, out var v)` | Lowered via statement-level pattern to `const v = map.get(k); if (v !== undefined) { … }` |

### Sets

`HashSet<T>` maps to a **custom** `HashSet<T>` class from `metano-runtime` that
supports structural equality via `equals()`/`hashCode()`. The plain `Set<T>`
built-in only does reference equality, which would lose C# semantics.

| C# | TypeScript |
|----|----|
| `HashSet<T>` / `ISet<T>` / `SortedSet<T>` / `ImmutableHashSet<T>` | `HashSet<T>` (from `metano-runtime`) |
| `set.Add(item)` | `set.add(item)` |
| `set.Contains(item)` | `set.has(item)` |
| `set.Remove(item)` | `set.delete(item)` |
| `set.Count` | `set.size` |

### Queues and stacks

| C# | TypeScript |
|----|----|
| `Queue<T>` | `T[]` (FIFO via `push`/`shift`) |
| `q.Enqueue(x)` | `q.push(x)` |
| `q.Dequeue()` | `q.shift()` |
| `q.Peek()` | `q[0]` |
| `Stack<T>` | `T[]` (LIFO via `push`/`pop`) |
| `s.Push(x)` | `s.push(x)` |
| `s.Pop()` | `s.pop()` |
| `s.Peek()` | `s[s.length - 1]` |

### Tuples and pairs

| C# | TypeScript |
|----|----|
| `Tuple<T1, T2>` / `ValueTuple<T1, T2>` / `(T1, T2)` | `[T1, T2]` |
| `KeyValuePair<K, V>` | `[K, V]` |

### IGrouping and other LINQ shapes

| C# | TypeScript |
|----|----|
| `IGrouping<K, V>` | `Grouping<K, V>` (from `metano-runtime`) |
| `IReadOnlyCollection<T>` | `Iterable<T>` |

## LINQ

When the transpiler detects LINQ extension method chains on any collection type,
it wraps the sequence in `Enumerable.from(...)` and emits lazy method chains.

```csharp
// C#
var result = items
    .Where(i => i.IsActive)
    .OrderByDescending(i => i.Score)
    .Take(10)
    .ToArray();
```

```typescript
// TypeScript
const result = Enumerable.from(items)
  .where((i) => i.isActive)
  .orderByDescending((i) => i.score)
  .take(10)
  .toArray();
```

### Composition operators

`where`, `select`, `selectMany`, `orderBy`, `orderByDescending`, `thenBy`,
`thenByDescending`, `take`, `takeWhile`, `skip`, `skipWhile`, `distinct`,
`distinctBy`, `groupBy`, `concat`, `reverse`, `zip`, `append`, `prepend`, `union`,
`intersect`, `except`

### Terminal operators

`toArray`, `toMap` (from `ToDictionary`), `toSet` (from `ToHashSet`), `first`,
`firstOrDefault`, `last`, `lastOrDefault`, `single`, `singleOrDefault`, `any`,
`all`, `count`, `sum`, `average`, `min`, `max`, `minBy`, `maxBy`, `contains`,
`aggregate`

### Direct method calls (fast path)

For simple operations that don't need LINQ wrapping, the transpiler uses the
native array methods directly:

| C# | TypeScript |
|----|----|
| `list.Contains(x)` | `list.includes(x)` |
| `list.Find(p)` | `list.find(p)` |
| `list.IndexOf(x)` | `list.indexOf(x)` |

## Tasks and async

| C# | TypeScript |
|----|----|
| `Task` | `Promise<void>` |
| `Task<T>` | `Promise<T>` |
| `ValueTask<T>` | `Promise<T>` |
| `Task.CompletedTask` | `Promise.resolve()` |
| `Task.FromResult(x)` | `Promise.resolve(x)` |
| `async Task<T> Foo()` | `async foo(): Promise<T>` |
| `await expr` | `await expr` |

## String methods

| C# | TypeScript |
|----|----|
| `s.ToUpper()` | `s.toUpperCase()` |
| `s.ToLower()` | `s.toLowerCase()` |
| `s.Trim()` | `s.trim()` |
| `s.Contains(sub)` | `s.includes(sub)` |
| `s.StartsWith(p)` | `s.startsWith(p)` |
| `s.EndsWith(p)` | `s.endsWith(p)` |
| `s.Substring(i)` / `s.Substring(i, n)` | `s.substring(i)` / `s.substring(i, i+n)` |
| `s.Replace(a, b)` | `s.replaceAll(a, b)` |
| `s.Split(c)` | `s.split(c)` |
| `s.Length` | `s.length` |
| `string.IsNullOrEmpty(s)` | `(s == null \|\| s.length === 0)` |
| `string.Join(sep, items)` | `items.join(sep)` |
| `int.Parse(s)` | `Number.parseInt(s, 10)` |

## Math

All `Math.X(...)` methods map directly to JavaScript `Math.x(...)`:

| C# | TypeScript |
|----|----|
| `Math.Abs(x)` | `Math.abs(x)` |
| `Math.Floor(x)` | `Math.floor(x)` |
| `Math.Ceiling(x)` | `Math.ceil(x)` |
| `Math.Round(x)` | `Math.round(x)` |
| `Math.Sqrt(x)` | `Math.sqrt(x)` |
| `Math.Max(a, b)` | `Math.max(a, b)` |
| `Math.Pow(a, b)` | `Math.pow(a, b)` |
| `Math.PI` | `Math.PI` |

## Console

| C# | TypeScript |
|----|----|
| `Console.WriteLine(x)` | `console.log(x)` |
| `Console.Error.WriteLine(x)` | `console.error(x)` |

## Enums

| C# | TypeScript |
|----|----|
| `Enum.Parse<T>(s)` | `T[s as keyof typeof T]` |
| `enumValue.HasFlag(flag)` | `(enumValue & flag) === flag` |
| `[Flags] enum` | numeric TS `enum` with bitwise operators |

## Adding your own mappings

If none of the built-in mappings fit, you have several escape hatches:

### 1. `[MapMethod]` / `[MapProperty]` — declarative

Add assembly-level mappings to define new BCL lowerings without touching the
compiler. See [Attributes Reference](attributes.md#declarative-bcl-mappings).

### 2. `[Emit]` — inline JS at call sites

For one-off cases, decorate a method with `[Emit("$0.foo($1)")]` and the call is
replaced verbatim at the call site.

### 3. `[Import]` — wrap an npm module

Declare a C# facade with `[Import(from: "some-pkg")]` and use it as a normal
type. The transpiler emits import statements and package.json entries but never
transpiles the declaration itself.

## See also

- [Attribute Reference](attributes.md) — `[MapMethod]`, `[Emit]`, `[Import]` details
- [Architecture Overview](architecture.md) — how the mapping registry works internally
