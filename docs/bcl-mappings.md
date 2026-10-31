# BCL Type Mappings

Metano maps many C# standard library types to idiomatic target equivalents. This
reference describes the mature TypeScript backend; the Dart backend has partial
coverage and is tracked in [Better Flutter Support Plan](roadmap/better-flutter-support.md).
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
| `Math.Round(d)` / `decimal.Round(d, digits, mode)` | `roundDecimal(d)` / `roundDecimal(d, digits, mode)` (runtime) |
| `Math.Floor(d)` / `Math.Ceiling(d)` / `Math.Truncate(d)` | `d.floor()` / `d.ceil()` / `d.trunc()` |

`roundDecimal` calls `toDecimalPlaces` with the decimal.js rounding constant of the
`MidpointRounding` mode, defaulting to half-to-even as C# does (decimal.js `round()`
alone would round a midpoint away from zero).

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
  export function tryParse(text: string | null | undefined): readonly [found: boolean, value: UUID];
}
```

**Lowerings:**

| C# | TypeScript |
|----|----|
| `Guid.NewGuid()` | `UUID.newUuid()` |
| `Guid.NewGuid().ToString("N")` | `UUID.newUuid().replace(/-/g, "")` (compact form) |
| `Guid.Parse(s)` | `UUID.create(s)` |
| `Guid.TryParse(s, out var g)` | `const [ok, g] = UUID.tryParse(s)` (`D`/`N`/`B`/`P` formats, normalized to lowercase `D`; `UUID.empty` on failure) |
| `Guid.Empty` | `UUID.empty` |
| `guid.ToString()` | `guid` (identity — already a string) |

**Why branded?** The same rationale as `[Branded]` on user-defined IDs:
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
| `dict.Keys` / `dict.Values` | `[...map.keys()]` / `[...map.values()]` |
| `dict.Keys.Count` / `dict.Values.Count` | `map.size` |
| `dict.Keys.Contains(k)` | `map.has(k)` |
| `dict[key]` (read) | `map.get(key)` |
| `dict[key] = value` (write) | `map.set(key, value)` |
| `dict.TryGetValue(k, out var v)` | `const [found, v] = tryGetValue(map, k, <default(TValue)>)` (runtime; see [`out` parameters](#out-parameters-and-arguments)) |
| `dict.GetValueOrDefault(k)` | `getValueOrDefault(map, k, <default(TValue)>)` (runtime) |
| `dict.GetValueOrDefault(k, fallback)` | `getValueOrDefault(map, k, fallback)` (runtime) |

`Map.keys()` / `Map.values()` return one-shot iterators, while a C# view can be
enumerated many times and `IDictionary<K,V>.Keys` is an `ICollection<K>` (a TS array).
The views therefore lower to an array copy (typed `K[]` / `V[]` for
`Dictionary<K,V>.KeyCollection` / `ValueCollection`), so `foreach`, LINQ, `ToList()` and
`string.Join` over them work. Unlike the C# view, the copy does not follow later changes
to the dictionary. `dict.Keys.Count`, `dict.Values.Count` and `dict.Keys.Contains(k)`
never build the copy: they fold into `map.size` and `map.has(k)`. The `Count` of an
`IDictionary<K,V>` or `IReadOnlyDictionary<K,V>` (which C# binds to the inherited
`ICollection<KeyValuePair<K,V>>.Count`) maps to `map.size` as well.

`getValueOrDefault` tests membership with `has`, so a key stored with a `null` value
returns `null` rather than the default, as in C#. For the single-argument overload the
compiler passes `default(TValue)`: `0` for numeric types, `false`, `null` for reference
and nullable types, `new Decimal("0")`, `"\0"` for `char`, `UUID.empty` for `Guid`,
and the enum member whose value is `0`. Known gaps:

- An enum that declares no zero member receives a bare `0`, other structs (user structs,
  temporal types) receive `undefined`, and an unconstrained generic `TValue` receives
  `null`. The emitted call runs, but `tsc --strict` rejects it because the default is
  not assignable to `TValue` (#291).
- The null-conditional form `dict?.GetValueOrDefault(k)` goes through the same
  lowering, guarded: `dict == null ? null : getValueOrDefault(dict, k, 0)`.

### `out` parameters and arguments

TypeScript has no by-reference arguments, so a method with `out` parameters returns
them: the `out` parameters leave the parameter list and the method returns
`[result, out1, out2, …]` in parameter order (a `void` method returns only the `out`
values, as a tuple even for one). The return type is a labeled readonly tuple; for a
`bool` method whose `out` parameters carry `[NotNullWhen]` / `[MaybeNullWhen]`, a union
with one tuple per result, so destructuring with `const` narrows like C# flow analysis:

```ts
// bool TryFind(string key, [NotNullWhen(true)] out Item? item)
tryFind(key: string): readonly [found: true, item: Item] | readonly [found: false, item: Item | null]
```

Call sites bind the tuple ahead of the statement when the call runs first
(`if (x.TryFind(k, out var item))` → `const [$found0, item] = x.tryFind(k); if ($found0)`;
`var ok = …` binds `ok` itself). A `while` / `for` condition moves into the loop body
(`while (true) { const [$found0, v] = …; if (!$found0) break; … }`), so each iteration
binds fresh locals as in C#. In `if (a && x.TryFind(k, out var v) && …)` without an
`else`, the call is bound inside `if (a) { … }`. Anywhere else behind short-circuit or
conditional evaluation, the call stays in place and assigns a hoisted `let`:
`a && ([, v] = x.tryFind(k))[0]` — `v` then keeps its nullable type, without narrowing.
An explicitly typed non-nullable target (`out string s` for a `[MaybeNullWhen(false)]`
parameter) receives `string | null`, which `--strict` rejects where C# only warns. A
`return` inside a `try` whose `finally` assigns an `out` parameter reports MS0001: the
returned tuple holds the value before the `finally` runs. The Dart target does not lower
`out` parameters yet.

BCL methods with `out` parameters map through `[MapMethod]`; the template receives one
argument per `out` parameter holding C#'s `default(T)` for it (`0`, `false`, `null`,
`'\0'`, the zero enum member, `Guid.Empty`). A struct without a literal default
(`DateTime`, user structs) reports MS0001. `ref` arguments, and `out` arguments of
delegates, constructors and null-conditional calls, report MS0001 and pass by value.

| C# | TypeScript |
|----|----|
| `int.TryParse(s, out var n)` | `tryParseInt32(s)` |
| `long.TryParse(s, out var n)` | `tryParseInt64(s)` |
| `double.TryParse` / `float.TryParse` | `tryParseFloat64(s)` |
| `bool.TryParse(s, out var b)` | `tryParseBoolean(s)` |

The parsers follow the invariant culture and only the `(string, out T)` overloads are
mapped; `decimal.TryParse` is not mapped yet.

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
| `string.Join(sep, items)` (array, `List<T>`, `IReadOnlyList<T>`, …) | `items.join(sep)` |
| `string.Join(sep, items)` (other `IEnumerable<T>`) | `[...items].join(sep)` |
| `string.Join(sep, a, b, c)` (`params`) | `[a, b, c].join(sep)` |
| `int.Parse(s)` | `Number.parseInt(s, 10)` |

Each element is converted the way JS `Array.prototype.join` converts it, which is the
same conversion string interpolation and `ToString()` use elsewhere in the generated
code: `null` elements become empty strings, as in .NET, but `bool` values print as
`true` / `false` (C# prints `True` / `False`) and numbers use the JS number format
(`1e+21`, where C# prints `1E+21`).

## Math

Most `Math.X(...)` methods map directly to JavaScript `Math.x(...)`:

| C# | TypeScript |
|----|----|
| `Math.Abs(x)` | `Math.abs(x)` |
| `Math.Floor(x)` | `Math.floor(x)` |
| `Math.Ceiling(x)` | `Math.ceil(x)` |
| `Math.Truncate(x)` | `Math.trunc(x)` |
| `Math.Round(x)` | `roundNumber(x)` (runtime) |
| `Math.Round(x, digits)` | `roundNumber(x, digits)` (runtime) |
| `Math.Round(x, mode)` | `roundNumber(x, 0, mode)` (runtime) |
| `Math.Round(x, digits, mode)` | `roundNumber(x, digits, mode)` (runtime) |
| `Math.Sqrt(x)` | `Math.sqrt(x)` |
| `Math.Max(a, b)` | `Math.max(a, b)` |
| `Math.Pow(a, b)` | `Math.pow(a, b)` |
| `Math.PI` | `Math.PI` |

`Math.Round` cannot map to JS `Math.round`, which takes no digit count and rounds a
midpoint toward +Infinity (`Math.round(-2.5)` is `-2`), while C# rounds it to the
nearest even number (`Math.Round(2.5)` is `2`). The runtime `roundNumber` reproduces
.NET's algorithm (scale by `10^digits`, round, scale back), so its results match .NET
bit for bit, including `Math.Round(2.675, 2)` (`2.68`, because the double product `2.675 * 100`
rounds to `267.5`) and the sign of a zero result. Parity is pinned to the .NET 10
algorithm: .NET 11 rounds the exact binary value instead (`Math.Round(2.675, 2)` is `2.67`
there). `MidpointRounding` maps to
the runtime `MidpointRounding` object, which keeps the .NET member names and values.

## Floating point (`double` / `float`)

`double` and `float` both lower to `number`, so their static helpers and special
values map to JavaScript globals. The same mappings apply to `float.X`, except the
`Epsilon`/`MaxValue`/`MinValue` constants listed separately below.

| C# | TypeScript |
|----|----|
| `double.IsNaN(x)` | `Number.isNaN(x)` |
| `double.IsFinite(x)` | `Number.isFinite(x)` |
| `double.IsInfinity(x)` | `(Math.abs(x) === Infinity)` |
| `double.IsPositiveInfinity(x)` | `Object.is(x, Infinity)` |
| `double.IsNegativeInfinity(x)` | `Object.is(x, -Infinity)` |
| `double.NaN` | `Number.NaN` |
| `double.PositiveInfinity` | `Infinity` |
| `double.NegativeInfinity` | `Number.NEGATIVE_INFINITY` |
| `double.Epsilon` | `Number.MIN_VALUE` |
| `double.MaxValue` / `double.MinValue` | `Number.MAX_VALUE` / `(-Number.MAX_VALUE)` |
| `float.Epsilon` | `1.401298464324817e-45` |
| `float.MaxValue` / `float.MinValue` | `3.4028234663852886e+38` / `(-3.4028234663852886e+38)` |

Each argument stays in a call-argument position (or the template parenthesizes
itself), so composite arguments keep their meaning: `double.IsInfinity(a ?? b)`
becomes `(Math.abs(a ?? b) === Infinity)`, and the argument is evaluated once.
Negative constants use a named property or parentheses so a unary minus at the call
site cannot produce `--`. `float` constants are the exact double value of the
corresponding `float`.
A constant pattern against NaN (`x is double.NaN`, a `double.NaN =>` switch arm)
lowers to `Number.isNaN(x)`, because `x === NaN` is always false in JavaScript;
plain equality `x == double.NaN` stays `x === Number.NaN`, which is also false in .NET.
`NaN` is written `Number.NaN` because TypeScript rejects a comparison against the
bare `NaN` global (TS2845).

Not mapped: `IsNormal`/`IsSubnormal`; method groups such as
`Func<double, bool> f = double.IsNaN` (templates apply to calls only); and generic
math calls such as `T.IsNaN(v)` with `T : INumber<T>`, which bind to the generic
interface rather than to `double`/`float`.

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

## Null-conditional access

Every mapping above also applies to a member read through `?.` or `?[…]`:
the steps after the `?.` are lowered like any other member access, and the
receiver is evaluated once.

| C# | TypeScript |
|----|----|
| `value?.Trim() ?? string.Empty` | `value?.trim() ?? ""` |
| `list?.Count` | `list?.length` |
| `order?.Items.Remove(x)` | `order == null ? null : listRemove(order.items, x)` |
| `Find()?.Items.Remove(x)` | `(($receiver) => $receiver == null ? null : listRemove($receiver.items, x))(find())` |
| `order?.Items.Clear();` (statement) | `if (order != null) { order.items.length = 0; }` |

Native optional chaining is kept when every step is a plain property, element
access or call; a step mapped to a template or a helper that takes the
receiver as an argument guards the receiver instead.

**Known deviation:** a native optional chain yields `undefined` for a null
receiver, where C# (and the guarded forms) yield `null`, so a declared
`T | null` result fails `tsc --strict` on the native forms (#256).

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
