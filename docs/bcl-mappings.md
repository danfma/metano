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
| `int`, `short`, `byte`, `sbyte`, `uint`, `ushort`, `float`, `double` | `number` ([integers of 32 bits or fewer](#integers-of-32-bits-or-fewer)) |
| `long`, `ulong`, `nint`, `nuint` | `bigint` (3.0.0; [64-bit integers](#64-bit-integers-long--ulong); 2.x mapped them to `number`) |
| `decimal` | `Decimal` (from `metano-runtime/system/decimal`: [`decimal.js`](https://mikemcl.github.io/decimal.js/) configured for `System.Decimal`) |
| `BigInteger` | `bigint` (`new BigInteger(x)` from `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal` and the default one lower to `BigInt(x)` / `bigIntegerFromDouble(x)` / `bigIntegerFromDecimal(x)` / `0n`; the byte-array and span overloads are not mapped) |
| `char` | `string` (length 1) |
| `Guid` | `UUID` (branded `string`, from `metano-runtime`) |
| `Uri` | `string` |
| `object` | `Object` (any non-nullish value; see [`object` and `Object`](#object-and-object)) |
| `object?` | `Object \| null` (the C# `null` maps to `null`, never `undefined`) |
| `void` | `void` |
| `T?` / `Nullable<T>` | `T \| null` |

### `object` and `Object`

C# `object` maps to the TypeScript `Object` type and `object?` maps to `Object | null`; the two are kept distinct, following the general nullable rule (`T?` → `T | null`). This is the closest match to C#'s semantics:

- `Object` accepts every non-nullish value, primitives included, as C# `object` accepts boxed `int`, `string` and so on. The lowercase `object` type would reject a boxed number.
- The non-nullable / nullable split stays visible. `unknown` would include `null` and `undefined` and erase it.
- `Object.prototype` members such as `toString` type-check without casts, which the generated code relies on for `ToString()`, `Equals` and `GetHashCode`.

Linters that ban the `Object` type (Biome's `lint/complexity/noBannedTypes`, typescript-eslint's `no-wrapper-object-types`) flag generated files. Exclude the generated output folder from those rules rather than editing generated code; for example, in `biome.json`:

```json
{
  "overrides": [
    {
      "includes": ["src/generated/**"],
      "linter": { "rules": { "complexity": { "noBannedTypes": "off" } } }
    }
  ]
}
```

### `decimal` specifics

Because JavaScript numbers are IEEE-754 (and lose precision for many financial
calculations), `decimal` maps to the `Decimal` class of `metano-runtime/system/decimal`:
[`decimal.js`](https://mikemcl.github.io/decimal.js/) configured as a clone with
`System.Decimal`'s 29 significant digits and half-to-even rounding, where decimal.js defaults to
20 digits and rounds a midpoint half up (baseline FR-055, #465). `decimal.MaxValue - 1` is
`79228162514264337593543950334` and `0.5m + 2.5m` rounds as .NET does; decimal.js is a dependency
of `metano-runtime`, so a project no longer lists it. Known differences from `System.Decimal`
(arithmetic rows against real .NET: `tests/BclReference`): a quotient keeps 29 significant digits
where .NET keeps 28 decimal places (`1m / 3m` has one more `3`), a result below `1e-28` is kept
where .NET rounds it to zero, trailing zeros are dropped (`1.10m` prints `1.1`; #662), and
overflow past `decimal.MaxValue` does not throw. A project that declares its own
`[ExportFromBcl(typeof(decimal), FromPackage = "decimal.js", ...)]` keeps the unconfigured
class. Operators are lowered to method calls:

| C# | TypeScript |
|----|----|
| `a + b` | `a.plus(b)` |
| `a - b` | `a.minus(b)` |
| `a * b` | `a.times(b)` |
| `a / b` | `a.dividedBy(b)` |
| `a == b` | `a.equals(b)` |
| `a > b` | `a.greaterThan(b)` |
| `decimal.Zero` | `new Decimal(0)` |
| `decimal.Parse(s)` / `decimal.Parse(s, provider)` | `new Decimal(parseDecimalText(s))` (runtime: .NET's `NumberStyles.Number` grammar, `FormatException` / `OverflowException`; the provider is ignored) |
| `decimal.Parse(s, styles)` (and the span overloads) | `new Decimal(s)` (decimal.js's grammar, which accepts exponents like `NumberStyles.Float`; a malformed text throws decimal.js's `DecimalError`) |
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
// a companion object over module functions (UUID$create, …)
export const UUID: {
  create(value: string): UUID;
  newUuid(): UUID;
  newCompact(): UUID;
  empty: UUID;
  isUuid(value: unknown): value is UUID;
  tryParse(text: string | null | undefined): readonly [found: boolean, value: UUID];
  // …
};
```

**Lowerings:**

| C# | TypeScript |
|----|----|
| `Guid.NewGuid()` | `UUID.newUuid()` |
| `Guid.NewGuid().ToString("N")` | `UUID.newUuid().replace(/-/g, "")` (compact form) |
| `g.ToString("B")` / `g.ToString("P")` | `("{" + g + "}")` / `("(" + g + ")")` (also `b`, `p`, `n`) |
| `Guid.Parse(s)` | `UUID.parse(s)` (`D`/`N`/`B`/`P` formats, any letter case, normalized to lowercase `D`; `FormatException` for anything else; baseline FR-054, #673) |
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

**TypeScript 7.** `@js-temporal/polyfill` 0.5.1 declares an `Intl.DateTimeFormat` that clashes with
the built-in `Temporal`/`Intl` types of TypeScript 7 under `lib: esnext`, so a package that imports
`metano-runtime` or a generated Temporal type fails `tsgo --strict` with TS2430 inside
`node_modules/@js-temporal/polyfill/index.d.ts` unless it sets `"skipLibCheck": true` or a `lib`
without `esnext.temporal` (for example `es2024`). TypeScript 5.9 is not affected (baseline
FR-107, #786).

| C# | TypeScript |
|----|----|
| `DateTime` | `Temporal.PlainDateTime` |
| `DateTimeOffset` | `Temporal.ZonedDateTime` |
| `DateOnly` | `Temporal.PlainDate` |
| `TimeOnly` | `Temporal.PlainTime` |
| `TimeSpan` | `Temporal.Duration` |
| `DateTime.Now` / `DateTimeOffset.UtcNow` | `Temporal.Now.plainDateTimeISO()` / `Temporal.Now.zonedDateTimeISO()` |
| `DateOnly.DayNumber` | `dayNumber(date)` (runtime helper) |
| `new TimeSpan(h, m, s)` / `(d, h, m, s)` / `(d, h, m, s, ms)` / `(d, h, m, s, ms, µs)` | `timeSpanFromParts(0, h, m, s)` / `timeSpanFromParts(d, h, m, s)` / … (runtime; `new Temporal.Duration(0, 5, 0)` would be five *months*) |
| `TimeSpan.Zero` | `timeSpanZero` |
| `TimeSpan.FromDays(x)` … `FromMicroseconds(x)` (one `double`, `int` or `long` argument) | `timeSpanFromDays(x)` … `timeSpanFromMicroseconds(x)` |
| `new DateTimeOffset(y, mo, d, h, mi, s, offset)` / `(…, ms, offset)` | `dateTimeOffset(y, mo, d, h, mi, s, 0, offset)` / `dateTimeOffset(…, ms, offset)` |
| `new DateTimeOffset(dateTime, offset)` / `(ticks, offset)` | `dateTimeOffsetFromDateTime(dateTime, offset)` / `dateTimeOffsetFromTicks(ticks, offset)` |

The constructors and factories above (baseline FR-054, #673) check .NET's ranges and throw its
exceptions: `TimeSpan` components beyond `TimeSpan.MaxValue` throw `ArgumentOutOfRangeException`;
`FromX(double)` truncates a fraction toward zero, as .NET does, and throws `ArgumentException`
for NaN and `OverflowException` past the range; a `long` argument past the range throws
`ArgumentOutOfRangeException`, as in .NET, but an `int` argument (`FromDays(int.MaxValue)`)
throws `OverflowException` where .NET throws `ArgumentOutOfRangeException`, because a `number`
carries no static type to tell the `int` overload from the `double` one. The .NET 9 `FromHours(2, 30)` forms that combine units stay
unmapped (MS0001). `DateTimeOffset` takes an offset in whole minutes within fourteen hours
(`ArgumentException` / `ArgumentOutOfRangeException` otherwise) and a UTC time inside years 1 to
9999; a `DateTime` argument carries no kind in TypeScript, so the UTC-kind offset check is
skipped.

### `DateTime` with a `DateTimeKind` (#753)

| C# | TypeScript |
|----|----|
| `new DateTime(y, mo, d, h, mi, s, kind)` / `(…, ms, kind)` | `plainDateTime(y, mo, d, h, mi, s)` / `plainDateTime(…, ms)` |
| `new DateTime(ticks, kind)` | `plainDateTimeFromTicks(ticks)` |

A `Temporal.PlainDateTime` carries no kind, so the `kind` argument is dropped: the value keeps
its wall time, but a `Utc` value serialized to JSON writes `2024-03-05T10:20:30` where .NET
writes `2024-03-05T10:20:30Z`. `DateTime.Kind`, `ToUniversalTime()`, `ToLocalTime()` and
`DateTime.SpecifyKind(...)` have no counterpart and report MS0001.

The `@js-temporal/polyfill` dependency is added to your `package.json`
automatically whenever any Temporal mapping is used.

## Collections

### Runtime collection classes (3.0.0)

`List<T>`, `Dictionary<K, V>`, `HashSet<T>`, `Queue<T>` and `Stack<T>` are
`metano-runtime` classes (`metano-runtime/system/collections`), and their
interfaces are the TypeScript interfaces of the same name; a `T[]` stays a native
array. The rows of the table below that name `List<T>` describe the 2.x lowering
and are superseded by this subsection.

| C# | TypeScript |
|----|----|
| `List<T>` / `IList<T>` / `IReadOnlyList<T>` | `List<T>` / `IList<T>` / `IReadOnlyList<T>` |
| `Dictionary<K, V>` / `IDictionary<K, V>` / `IReadOnlyDictionary<K, V>` | `Dictionary<K, V>` / `IDictionary<K, V>` / `IReadOnlyDictionary<K, V>` |
| `HashSet<T>` / `ISet<T>` / `IReadOnlySet<T>`, `Queue<T>`, `Stack<T>` | the class or interface of the same name |
| `list[i]`, `list[i] += v`, `list[^1]`, `list[a..b]` | `list.get(i)`, `list.set(i, list.get(i) + v)`, `list.fromEnd(1)`, `list.slice(a, b)` |
| `new List<T> { a, b }`, `[a, b]` for a list | `List.of([a, b])` |
| `new Dictionary<K, V> { ["k"] = v }` / `{ { "k", v } }` | `Dictionary.assign([["k", v]])` / `Dictionary.of([["k", v]])` |
| `list.ToArray()` | `list.toArray()` (a copy); `AsJsArray()` / `AsSpan()` are the zero-copy views |
| `xs.ToList()`, `xs.ToDictionary(...)`, `xs.ToHashSet()` | the LINQ terminals `toList()`, `toDictionary(...)`, `toHashSet()`, which build the runtime classes |
| `o is List<int>`, `o is IList<int>`, `o is Dictionary<string, int>`, type patterns and `as` | `isType(o, listType(Int32Type))`, `isType(o, iListType(Int32Type))`, `isType(o, dictionaryType(StringType, Int32Type))` |

**Type arguments at runtime.** When the compilation asks a runtime question about a
collection kind (a test, `as`, pattern, `GetType()` or `typeof` that names it, or an
interface it implements), every construction of that kind passes its type arguments as
the trailing argument (`new List<number>(null, [Int32Type])`, `List.of([1], [Int32Type])`,
`toList([Int32Type])`, `super(null, [Int32Type])` in a subclass of `List<int>`), and a
class argument gets its descriptor. A compilation that asks nothing emits no argument and
no reflection import. A test compares type arguments by identity for the invariant classes
and interfaces and by reference conversion for the covariant read-only interfaces
(`List<string>` is an `IReadOnlyList<object>`, `List<int>` is not). A collection built
without the slot (by a separately compiled library) or an open type parameter that is
not reified matches every argument (`MS0046` warns at the test).

**User implementers.** A transpiled class that implements `IList<T>` (or another collection
interface) has no collection descriptor, so `o is IList<int>` could answer `false` for it
where .NET answers `true`. The compiler reports `MS0001` at that test instead, and the site
throws at run time. An overload dispatcher's branch for such a parameter still tests the
runtime classes only.

### Additions after 3.0.0

| C# | TypeScript |
|----|----|
| `Array.Sort(a)`, `MemoryExtensions.Sort(span)` | `sortArray(a)` / `sortArrayByKeys(...)` (.NET ordering, not JS text order) |
| `Array.IndexOf(a, v)`, `LastIndexOf`, span `Contains` / `IndexOf` / `LastIndexOf` | `arrayIndexOf` / `arrayLastIndexOf` / `arrayContains` (value equality, NaN found) |
| `StringComparer.Ordinal` / `OrdinalIgnoreCase` | runtime singletons (lazy, tree-shakable) |
| `StringBuilder`, `Lazy<T>`, `TaskCompletionSource<T>` | runtime classes of the same name (`Lazy` ignores `LazyThreadSafetyMode`) |
| `KeyValuePair.Create(k, v)`, `EqualityComparer<T>.Default` | mapped |
| `BigInteger.Zero/One/MinusOne`, `IsZero/IsOne/IsEven` | `0n` / `1n` / `-1n` and the matching predicates |
| `char.Is*` (11 predicates), `Convert.ToDouble` (int-sized, `double`, `float`, `decimal`, `bool`, `string`), `decimal.ToDouble` | mapped |
| `int.CompareTo` | `int`, `uint`, `double`, `float` give -1/0/1 (NaN first); `short`, `ushort`, `byte`, `sbyte`, `char` give the difference, as .NET |
| LINQ `ElementAt`, `ElementAtOrDefault`, `DefaultIfEmpty`, `Distinct(comparer)` | `elementAt`, `elementAtOrDefault`, `defaultIfEmpty`, `distinct(comparer)` |
| `ReadOnlyJsArray<T>`, `list.AsReadOnlyJsArray()` / `ro.AsReadOnlyJsArray()`, `ro.ToJsArray()` (#737) | `readonly T[]` (`IEnumerable<T>`-only in C#, not `IReadOnlyList<T>`; `[JsonConverter]` array support in STJ), `asReadOnlyJsArray(x)` (live zero-copy view for runtime array-backed lists; `Array.from(x)` shallow snapshot copy for receivers lacking `asReadOnlyJsArray()`, such as user `IReadOnlyList<T>` classes or `ReadOnlyCollection<T>` over a custom `IList<T>`), `ro.toArray()` (writable copy); TS-only (`MS0051` elsewhere). Do not add/remove via `list.AsJsArray()` while enumerating `list` (bypasses `_version`) |

LINQ searches use .NET's `EqualityComparer<T>.Default` through the runtime (#664): a record, `NaN`
and a decimal compare by value, and a class without `Equals` by reference.

| C# | TypeScript |
| --- | --- |
| `xs.Contains(x[, comparer])` on a lazy sequence | `linq(xs, contains(x[, comparer]))` |
| `xs.GroupBy(k[, comparer])` | `linq(xs, groupBy(k[, comparer]))`: groups and elements in first-seen order |
| `xs.SequenceEqual(ys[, comparer])` | `linq(xs, sequenceEqual(ys[, comparer]))` |
| `xs.ToLookup(k[, e][, comparer])` | `linq(xs, toLookup(k[, e][, comparer]))`: a runtime `Lookup` (`count`, `get`, `contains`, iteration) |
| `ILookup<K, T>`, `lookup[key]`, `lookup.Count`, `lookup.Contains(key)` | `Lookup<K, T>`, `lookup.get(key)` (an empty sequence for a missing key, as .NET), `lookup.count`, `lookup.contains(key)` |
| `xs.Join(ys, ok, ik, result[, comparer])` | `linq(xs, join(ys, ok, ik, result[, comparer]))`: a `null` key matches nothing |

Not mapped, reported with `MS0001`: `GroupJoin`, and the
`GroupBy` overloads with an element or result selector.

Not mapped, reported with `MS0001`: `ElementAt(Index)`, `Array.IndexOf(a, v, start, count)`,
`Array.Sort(a, index, length)`. Known gaps: `(decimal)double` emits the raw double; a record has no
`toString`; `-0` dictionary keys and `list.Equals` differ from .NET; `ReadOnlyDictionary`,
`FrozenDictionary`, `nint` / `nuint` members and the non-generic `TaskCompletionSource` are unmapped.
A cast of a set, queue or dictionary to `IList<T>` passes through (known gap, #710). Other numeric details:
`decimal` in a module function, a branded type or a JSX body is spelled `Decimal`; `decimal` to a
narrow integer is `narrowFromDecimal(d, 32, true)`; a record hashes a tuple-typed member by its elements, while `HashCode.add` still hashes an array by reference.

### Lists and arrays

| C# | TypeScript |
|----|----|
| `T[]` | `T[]` |
| `List<T>` / `IList<T>` / `IReadOnlyList<T>` | see above (`T[]` before 3.0.0) |
| `List<T>.Add(item)` | `list.add(item)` |
| `List<T>.Contains(item)` | `list.contains(item)` |
| `List<T>.IndexOf(item)` | `list.indexOf(item)` |
| `List<T>.Count` | `list.count` |
| `List<T>.Remove(item)` | `list.remove(item)` |
| `List<T>.ToArray()` | `list.toArray()` |
| `array.Contains(item)` (`MemoryExtensions`) | `array.includes(item)` |
| `array.IndexOf(item)` / `array.LastIndexOf(item)` (`MemoryExtensions`) | `array.indexOf(item)` / `array.lastIndexOf(item)` |
| `array.IndexOf(item, null)` (a `null` constant comparer, also `Contains` / `LastIndexOf`) | `array.indexOf(item)` |
| `span.StartsWith(s)` / `EndsWith(s)` / `IndexOf(s)` / `LastIndexOf(s)` / `Trim()` / `TrimStart()` / `TrimEnd()` on a `ReadOnlySpan<char>` | the string method of the same name |
| `span.Reverse()` on a `Span<T>` | `span.reverse()` (in place) |
| `array.Sort(comparison)` / `span.Sort(comparison)` | `array.sort(comparison)` (in place) |
| any other `MemoryExtensions` call | `MS0001`, throws at run time |

C# 14 converts an array (and a string) to `ReadOnlySpan<T>` / `Span<T>`
implicitly, so these calls on a `T[]` bind to the span extensions of
`System.MemoryExtensions`, not to `System.Linq.Enumerable`
(`src/Metano/Runtime/Spans.cs`). A span lowers to what it views: an array, or
the string a `ReadOnlySpan<char>` was converted from.

- `Contains` / `IndexOf` / `LastIndexOf` with the value alone lower to the
  `List<T>` methods. A comparer that is omitted or the `null` constant
  (`null`, `default`, a cast, a `const`) is dropped from every
  `MemoryExtensions` call, also when a named argument is written (`nullables.Contains(value: x)` → `nullables.includes(x)`; an
  `int?[]` receiver binds the comparer overload because `int?` is not
  `IEquatable<T>`).
- The string searches (`StartsWith`, `EndsWith`, the parameterless `Trim`
  family, the subsequence `IndexOf` / `LastIndexOf`) lower to the string
  methods only on a `string` or `ReadOnlySpan<char>` receiver (a cast such as
  `((string)o).StartsWith(span)` counts as a `string`). On an array
  (`char[]` included) or a `Span<char>` they report: `array.IndexOf(otherArray)`
  and `chars.IndexOf("bc")` search for a sequence, which `Array.indexOf` does
  not.
- Every other `MemoryExtensions` call reports `MS0001` and emits an expression
  that throws: a comparer that is not the `null` constant, a `StringComparison`
  overload, `SequenceEqual`, the parameterless `Sort()` (its previous
  `array.sort()` compared the elements as strings: right on the inputs tried
  for `char[]`, `bool[]`, `DateOnly`, `DateTime`, `TimeOnly` and small numeric
  enums, wrong for numbers from 10 up, `long`, `decimal`, `string`, string
  enums, `bool?` and `Guid`; every element type reports until the runtime
  sort helper lands, #663; pass a comparison instead, such as
  `(x, y) => x - y` for numbers or `(x, y) => x < y ? -1 : x > y ? 1 : 0` for
  `char`), `Sort(comparer)`, `BinarySearch`, `IndexOfAny`,
  `ContainsAny`, `Count(value)`, `AsSpan`, `Trim(c)`, the static form
  `MemoryExtensions.Contains(array, x)`, and a string receiver such as
  `text.Count('a')`.
- A lookup over a tuple element (`pairs.Contains((3, 4))`) lowers and reports
  `MS0001`, as on `List<(int, int)>`; a tuple search the rules above report is
  reported once.

`includes` / `indexOf` / `lastIndexOf` compare like `List<T>.Contains` /
`IndexOf`: by value for numbers, `bigint`s and strings, by reference for
records, record structs, `decimal` and `DateTime` values (C# compares them by
value), and `indexOf` / `lastIndexOf` do not find `NaN` (C# finds it;
`includes` does). `sort(comparison)` is stable where .NET's sort is not.

Known limitations, emitted with no diagnostic (as before #657):

- A `ReadOnlySpan<char>` over a `char[]` lowers to an array. Its
  `StartsWith`, `EndsWith` and `Trim` family throw (`Array` has none of
  them), and `IndexOf("ab")` / `LastIndexOf("ab")` call `Array.indexOf`,
  which looks for the string as an element and returns `-1`.
- A string searched for a `char[]` (`text.IndexOf(chars)`,
  `text.StartsWith(chars)`) passes the array to the string method, which
  compares with its string form (`"c,a"`) and answers wrongly.
- A `Memory<T>.Span` receiver throws: `Memory<T>` is not mapped.

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
| `long.TryParse(s, out var n)` | `tryParseInt64(s)` (returns `[found, bigint]`; see [64-bit integers](#64-bit-integers-long--ulong)) |
| `double.TryParse` / `float.TryParse` | `tryParseFloat64(s)` |
| `bool.TryParse(s, out var b)` | `tryParseBoolean(s)` |

The parsers follow the invariant culture. `decimal.TryParse(s, out var d)` is
`tryParseDecimal(s, Decimal)`, which applies the grammar of `decimal.Parse` and reports a
failure (`null` included) as `[false, 0]`; it takes the `Decimal` class as an argument, so the
runtime itself never imports decimal.js (baseline FR-054, #673).

### Parsing numbers and booleans

`Parse` and `TryParse` of `int`, `uint`, `short`, `ushort`, `byte` and `sbyte`, and `Parse` of
`double`, `float` and `bool`, call `metano-runtime` parsers with .NET's grammar and exceptions
(baseline FR-054, #668): `int.Parse("12abc")` throws `FormatException` where `Number.parseInt`
returns `12`, and `byte.Parse("300")` throws `OverflowException` with .NET's message.

| C# | TypeScript |
|----|----|
| `int.Parse(s)` / `int.Parse(s, provider)` | `parseInt32(s)` |
| `int.Parse(s, NumberStyles.HexNumber)` | `parseInt32(s, 515)` (the constant `NumberStyles` folds to its flags value, as for `long`) |
| `uint.Parse(s)` / `short.Parse(s)` / `ushort.Parse(s)` / `byte.Parse(s)` / `sbyte.Parse(s)` | `parseUInt32(s)` / `parseInt16(s)` / `parseUInt16(s)` / `parseByte(s)` / `parseSByte(s)` |
| `int.TryParse(s, NumberStyles.Integer, provider, out var n)` | `tryParseInt32(s, 7)` |
| `double.Parse(s)` / `float.Parse(s)` | `parseFloat64(s)` |
| `bool.Parse(s)` | `parseBoolean(s)` |

The styles are the ones `long.Parse` takes: `Integer`, `HexNumber` (a negative value reads as
two's complement at the type's width), `Integer | AllowThousands` and `Number`; any other
constant style reports `MS0066` and throws `NotSupportedException` at run time. The
`ReadOnlySpan<char>` forms of `Parse`, the `NumberStyles` forms of `double.Parse` and the
`(string, out T)` fallbacks of other types stay as they were (MS0001 for a static member with no
mapping).

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
| `KeyValuePair.Create(k, v)` | `[k, v] as [K, V]` |

Tuples are plain JS arrays, which the runtime collections compare by reference (a C# array
also lowers to an array, and C# compares those by reference too). A tuple used as a
`HashSet` element or `Dictionary` key, in `List` lookups (`Contains`, `IndexOf`, `Remove`) or
as the element or key of a LINQ operator that compares by default (`Distinct`, `DistinctBy`,
`GroupBy`, `Except`, `Intersect`, `Union`, `ToHashSet`, `ToDictionary`, `ToLookup`, `Join`,
`GroupJoin`, `SequenceEqual`, `Contains`; nullable tuples included) reports MS0001, because two
equal tuples built separately are not found (#746). Pass an `IEqualityComparer` or use a
record as the element or key.

### IGrouping and other LINQ shapes

| C# | TypeScript |
|----|----|
| `IGrouping<K, V>` | `Grouping<K, V>` (from `metano-runtime`) |
| `IReadOnlyCollection<T>` | `Iterable<T>` |

## LINQ

LINQ chains lower to the `linq(source, op1(...), op2(...))` pipe form of
`metano-runtime/system/linq`, whose overloads are listed under
[Supported overloads](#supported-overloads). The static call form
(`Enumerable.Where(xs, p)`, `Enumerable.Count(xs)`) lowers to the same pipe
(#657); a static call that names an argument reports `MS0001`.

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
const result = linq(
  items,
  where((i) => i.isActive),
  orderByDescending((i) => i.score),
  take(10),
  toArray(),
);
```

### Composition operators

`where`, `select`, `selectMany`, `orderBy`, `orderByDescending`, `thenBy`,
`thenByDescending`, `take`, `takeWhile`, `skip`, `skipWhile`, `distinct`,
`distinctBy`, `groupBy`, `concat`, `reverse`, `zip`, `append`, `prepend`, `union`,
`intersect`, `except`, `defaultIfEmpty`

`union`, `intersect` and `except` yield distinct elements compared by value, in
.NET's order (#657).

### Terminal operators

`toArray`, `toMap` (from `ToDictionary`), `toSet` (from `ToHashSet`), `first`,
`firstOrDefault`, `last`, `lastOrDefault`, `single`, `singleOrDefault`, `any`,
`all`, `count`, `sum`, `average`, `min`, `max`, `minBy`, `maxBy`, `contains`,
`aggregate`, `elementAt`, `elementAtOrDefault`

The `OrDefault` terminals take `(predicate?, defaultValue?)`. The compiler passes
`default(T)` for a non-nullable value element (`xs.FirstOrDefault()` over `int` is
`firstOrDefault(undefined, 0)`) and the `defaultValue` argument of the .NET 6
overloads; reference and nullable elements keep the `null` miss. An element whose
default no literal spells (a user struct, a tuple, `KeyValuePair` (so
`dict.FirstOrDefault()`), `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`,
`TimeOnly`, an `[InlineWrapper]` struct, a `struct` type parameter, a
`[StringEnum]` without a zero member) keeps `null` and reports `MS0001` (#621). A
type parameter that is not constrained to `struct` (unconstrained, `where T :
notnull`, `where T : IComparable<T>`, `where T : Enum` alone) also yields `null`,
without a warning. `ElementAt`, `ElementAtOrDefault` and `DefaultIfEmpty` lower to
`elementAt(index)`, `elementAtOrDefault(index, defaultValue?)` and
`defaultIfEmpty(defaultValue?)`, with the same `default(T)` rule (#617);
`ElementAt(^1)` (an `Index`) reports `MS0001`.

`Sum`, `Average`, `Min`, `Max`, `MinBy` and `MaxBy` are chosen by the element type
(FR-056, #618, #657): `min` / `max` / `minBy` / `maxBy` throw
`InvalidOperationException` on an empty sequence, and the `Nullable` variants
(`minNullable`, `maxByNullable`, `averageNullable`, ...) skip nulls and give `null`
for a `Nullable<T>` or reference result; `decimal` uses `sumDecimal` /
`averageDecimal` / `averageNullableDecimal` (decimal.js precision, so an average that
.NET computes to 28 digits has decimal.js's 20 by default). `double` `NaN` wins `Min`
and loses `Max`, as in .NET. Divergence: strings (and every `Min` / `Max` / `MinBy` /
`MaxBy` over strings) order by code unit, where .NET's default comparer is
culture-sensitive (`["b","A","a","B"].Min()` is `"a"` on .NET, `"A"` here; #663).
`Sum` of `int` is not overflow-checked.

### Supported overloads

A stage lowers only the overloads its runtime operator implements, named here by
their parameters after the source (the `Queryable` spellings `source2` and `item`
are the same overloads):

| Operator | Supported overloads |
|----|----|
| `Where`, `TakeWhile`, `SkipWhile` | `(predicate)`, with or without the element index |
| `Select` | `(selector)`, with or without the element index |
| `SelectMany` | `(selector)` |
| `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `GroupBy`, `DistinctBy`, `MinBy`, `MaxBy` | `(keySelector)` |
| `Take`, `Skip` | `(count)` |
| `Distinct` | `()`, `(comparer)` |
| `Reverse`, `ToArray`, `ToList`, `ToHashSet` | `()` |
| `DefaultIfEmpty` | `()`, `(defaultValue)` |
| `ElementAt`, `ElementAtOrDefault` | `(index)` |
| `Concat` | `(second)` |
| `Append`, `Prepend` | `(element)` |
| `Zip` | `(second, resultSelector)` |
| `ToDictionary` | `(keySelector)`, `(keySelector, elementSelector)` |
| `First`, `Last`, `Single`, `Any`, `Count`, `LongCount` | `()`, `(predicate)` |
| `FirstOrDefault`, `LastOrDefault`, `SingleOrDefault` | `()`, `(predicate)`, `(defaultValue)`, `(predicate, defaultValue)` |
| `All` | `(predicate)` |
| `Sum`, `Min`, `Max`, `Average` | `()`, `(selector)` |
| `Contains` | `(value)` |
| `Aggregate` | `(seed, func)` |

Every other overload reports `MS0001` (a warning) at the method name and
lowers to an expression that throws at run time: for example `Aggregate(func)`,
`Aggregate(seed, func, resultSelector)`, `GroupBy` with an element or result
selector, the tuple `Zip(second)`, `Take(range)`, `SelectMany` with a result
selector (#648). Only `Distinct` takes a comparer (#687), so any other comparer argument
(`IEqualityComparer<T>` / `IComparer<T>`, as in `OrderBy(k, comparer)`,
`ToDictionary(k, comparer)`, `Contains(x, comparer)`,
`Min(comparer)`) reports `MS0001` at the argument, unless it is the `null`
constant (`null`, `default`, `(IComparer<int>?)null`): that asks for the
default comparison and lowers as the comparer-less overload. A comparer that is
`null` only at run time still reports. A queryable stage passes its expression
tree to an operator that reads one, drops it for an operator that reads none
(`queryable.Aggregate(seed, func)` runs `aggregate(seed, func)`), and reports
when the tree would land in another argument's slot
(`queryable.ToDictionary(k)`, where `toMap` would read it as the value
selector). A `System.Linq` method with neither a pipe operator nor a
declarative mapping for the overload called (`Join`, `GroupJoin`, `ToLookup`,
`SequenceEqual`, `Cast`, `OfType`, …) reports `MS0001`, in the
extension and in the static form, instead of becoming a member call (where
`xs.Join(…)` reached `Array.prototype.join`). The static form
(`Enumerable.Aggregate(xs, …)`) of an unimplemented overload reports the same
diagnostic. The Dart target does not lower LINQ chains (#549) and reports none
of these.

An overload is identified by its parameter names, not by its element types: the
aggregates pick their runtime variant by element type (see above).

### Direct method calls (fast path)

For simple operations that don't need LINQ wrapping, the transpiler uses the
native array methods directly:

| C# | TypeScript |
|----|----|
| `list.Contains(x)` | `list.includes(x)` |
| `list.Find(p)` | `(list.find(p) ?? null)`, or `?? default(T)` for a value element |
| `list.IndexOf(x)` | `list.indexOf(x)` |

## Tasks and async

| C# | TypeScript |
|----|----|
| `Task` | `Promise<void>` |
| `Task<T>` | `Promise<T>` |
| `ValueTask<T>` | `Promise<T>` |
| `Task.CompletedTask` | `Promise.resolve()` |
| `Task.FromResult(x)` | `Promise.resolve(x)` |
| `new TaskCompletionSource<T>()` | `new TaskCompletionSource<T>()` (`metano-runtime`; `.Task` is `.task`) |
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
| `s.Substring(i)` / `s.Substring(i, n)` | `stringSubstring(s, i)` / `stringSubstring(s, i, n)` (from `metano-runtime`) |
| `s.Remove(i)` / `s.Remove(i, n)` / `s.Insert(i, t)` | `stringRemove(s, i)` / `stringRemove(s, i, n)` / `stringInsert(s, i, t)` |
| `s.PadLeft(n)` / `s.PadRight(n, c)` | `stringPadLeft(s, n)` / `stringPadRight(s, n, c)` |
| `s.IndexOf(t, i)` / `s.IndexOf(t, i, n)` / `s.IndexOf(t, comparison)` | `stringIndexOf(s, t, i)` / `stringIndexOf(s, t, i, n)` / `stringIndexOf(s, t, 0, undefined, comparison)` |
| `s.LastIndexOf(t, i)` (and the count and comparison forms) | `stringLastIndexOf(s, t, i)` |
| `s.StartsWith(p, comparison)` / `s.EndsWith(p, comparison)` / `s.Contains(t, comparison)` | `stringStartsWith(s, p, comparison)` / `stringEndsWith(...)` / `stringContains(...)` |
| `s.Trim(c)` / `s.TrimStart(c1, c2)` / `s.TrimEnd(chars)` | `stringTrim(s, c)` / `stringTrimStart(s, c1, c2)` / `stringTrimEnd(s, ...chars)` |
| `s.Split(c)` / `s.Split(c, count)` / `s.Split(sep, options)` / `s.Split()` | `stringSplit(s, c, false)` / `stringSplit(s, c, false, count)` / `stringSplit(s, sep, false, undefined, options)` / `stringSplitChars(s)` |
| `s.ToCharArray()` / `s.ToCharArray(i, n)` | `stringToCharArray(s)` / `stringToCharArray(s, i, n)` |
| `string.Join(sep, values, i, n)` | `stringJoinRange(sep, values, i, n)` |
| `s.Replace(a, b)` (`char` or `string` overloads) | `stringReplace(s, a, b)` (from `metano-runtime`) |
| `s.Replace(a, b, comparison)` / `s.Replace(a, b, ignoreCase, culture)` | `stringReplace(s, a, b, comparison)`; a case-insensitive comparison matches `a` in any case, the culture is not read |
| `s.Length` | `s.length` |
| `string.IsNullOrEmpty(s)` | `isNullOrEmpty(s)` (from `metano-runtime`) |
| `string.IsNullOrWhiteSpace(s)` | `isNullOrWhiteSpace(s)` (from `metano-runtime`) |
| `string.Join(sep, items)` (array, `List<T>`, `IReadOnlyList<T>`, …) | `items.join(sep)` |
| `string.Join(sep, items)` (other `IEnumerable<T>`) | `[...items].join(sep)` |
| `string.Join(sep, a, b, c)` (`params`) | `[a, b, c].join(sep)` |
| `int.Parse(s)` | `parseInt32(s)` (see [Parsing numbers](#parsing-numbers-and-booleans)) |

The members of the table above that a JavaScript method of the same name reads differently
go through `metano-runtime` helpers (baseline FR-053, #661): C#'s `Substring(start, length)`
takes a length where `substring(start, end)` takes an end index (`"abcdef".Substring(1, 2)` is
`"bc"`, `substring(1, 2)` is `"b"`), `substring` clamps and swaps bad arguments where C# throws,
`Trim(char)` would trim white space instead, `Split(c, count)` keeps the rest of the string in
its last piece where `split(c, limit)` drops it, and `indexOf(t, comparison)` would read the
enum as a position. Each helper reads its arguments once and throws what .NET throws
(`ArgumentOutOfRangeException` and `ArgumentNullException`, with .NET's messages and parameter
names), checked against real .NET by `tests/BclReference` (bun and Node). Comparisons are
ordinal: the culture-sensitive `StringComparison` members behave as their ordinal
counterparts, and the ignore-case ones fold each code point to its ordinal uppercase, as
`OrdinalIgnoreCase` does. `StringComparison` and `StringSplitOptions` lower to runtime
mirrors of the enums. `Trim()`, `Split(char)`, `IndexOf(t)` and the other forms of the same
names that agree with JavaScript keep the plain method (`Trim()` trims U+0085, which .NET
treats as white space and JavaScript does not, and U+FEFF the other way round, as noted below
for `IsNullOrWhiteSpace`). `Split(params char[])` takes up to four expanded characters; the
span, culture and `AsSpan`/`Slice` forms are not mapped.

Each element is converted the way JS `Array.prototype.join` converts it, which is the
same conversion string interpolation and `ToString()` use elsewhere in the generated
code: `null` elements become empty strings, as in .NET. A `Join<T>` over `bool`, `double`
or `float` converts each element with the default-text helper (see
[Default text of `bool`, `double` and `float`](#default-text-of-bool-double-and-float)); other
numbers use the JS number format, which agrees with .NET for integers.

`stringReplace` replaces every occurrence, as C# does (JS `replace` stops at the first),
and inserts the replacement literally (JS `replace` and `replaceAll` expand `$&`-style
sequences). Like C#, it throws on an empty old value and treats a `null` replacement as
removal. `isNullOrWhiteSpace` tests .NET's white-space set, which differs from JS `trim()`
on U+0085 and U+FEFF. Both null checks are type predicates, so a `false` result narrows
the argument to `string`.

A static member of a BCL type with no mapping (`string.Compare(a, i, b, j, n)`) reports an
`MS0001` warning: the call is emitted by name and nothing declares it on the JS side.

### String equality and ordering

`string.Equals` and `string.Compare` lower to `metano-runtime` helpers (#769):

| C# | TypeScript |
|----|----|
| `string.Equals(a, b, comparison)` | `stringEquals(a, b, comparison)` |
| `a.Equals(b, comparison)` / `a.Equals(b)` | `stringInstanceEquals(a, b, comparison)` / `stringInstanceEquals(a, b, StringComparison.Ordinal)` |
| `string.Equals(a, b)` | `(a === b)` |
| `string.Compare(a, b, comparison)` | `stringCompare(a, b, comparison)` |
| `string.Compare(a, b)` | `stringCompare(a, b, StringComparison.CurrentCulture)` |
| `string.Compare(a, b, ignoreCase)` | `stringCompare(a, b, ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture)` |
| `string.CompareOrdinal(a, b)` | `stringCompare(a, b, StringComparison.Ordinal)` |
| `a.CompareTo(b)` (`string` or `object` argument) | `stringCompareTo(a, b)` |
| `a.GetHashCode(comparison)` | `stringHashCode(a, comparison)` |
| `IComparable<T>.CompareTo` / `IComparable.CompareTo` through the interface or a type parameter | `comparableCompareTo(a, b)` |

`Ordinal` compares UTF-16 code units. `OrdinalIgnoreCase` compares the ordinal uppercase of
each code point, .NET's invariant simple case mapping (the rule `StringComparer.OrdinalIgnoreCase`
and the case-insensitive searches above share): `ß` never equals `SS`, `ı` and `ſ` never equal
`I` and `S`, a surrogate pair folds as one code point, and Greek letters with iota subscript
map to their prosgegrammeni form. The culture members compare with `Intl.Collator`:
`CurrentCulture` with the engine's default locale, `InvariantCulture` with the root locale, at
`sensitivity: "variant"`. The ignore-case members ignore case only, as .NET does on ICU: width
and kana type still count (`"Ａ"` never equals `"a"`, `"ｶ"` sorts after `"カ"`), while
compatibility forms and case variants are equal (`ß` / `ẞ`, `ﬀ` / `FF`, `ς` / `Σ`, `"Ａ"` /
`"ａ"`, U+3000 / space). Five exotic pairs differ from .NET on ICU (known gaps): `İ` / `i̇`,
`ｶﾞ` / `ｶ゙` and U+FFE8 / `│` are equal in .NET and not in the runtime, and small against
full-size kana (`ｱ` / `ｧ`, `ア` / `ァ`) sort the other way. This
matches .NET running on ICU (the default on Linux and macOS); a .NET process with
`InvariantGlobalization` compares the culture members ordinally instead. `null` equals only
`null` and sorts first; the instance forms throw `NullReferenceException` for a `null`
receiver, and `CompareTo(object)` throws `ArgumentException` for an argument that is not a
string; an undefined `StringComparison` value throws `ArgumentException`. A `Compare` result
has .NET's sign; the ordinal forms return the difference of the first differing code units
(or of the lengths), since .NET does not specify the magnitude. `GetHashCode(comparison)`
hashes as the ordinal `StringComparer` does; a culture comparison throws
`NotSupportedException` (JavaScript has no culture sort key). `comparableCompareTo` compares a
string receiver as `string.CompareTo` does and any other value in `Comparer<T>.Default`'s
order, which calls an object's own `compareTo`; a `null` argument answers `1` without calling
it (the `IComparable` contract), so a user `CompareTo(null)` that breaks the contract is not
reproduced. The substring overload
`Compare(a, i, b, j, n, comparison)` is not mapped (`MS0001`).

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

### `Math.Clamp` and `HashCode` (#749, #750)

| C# | TypeScript |
|----|----|
| `Math.Clamp(v, lo, hi)` / `int.Clamp` / `double.Clamp` (every `number`-backed overload) | `clampNumber(v, lo, hi)` (runtime) |
| `Math.Clamp` / `decimal.Clamp` on `decimal` | `clampDecimal(v, lo, hi)` (runtime; keeps the `Decimal`) |
| `Math.Clamp` / `long.Clamp` on `long`, `ulong` | `clampInt64` / `clampUInt64` (runtime) |
| `HashCode.Combine(v1 … v8)` | `HashCode.combine(v1, …)` (runtime, variadic) |
| `new HashCode()`, `Add(v)`, `ToHashCode()` | `new HashCode()`, `add(v)`, `toHashCode()` |

JavaScript has no `Math.clamp`. Every clamp throws .NET's `ArgumentException` when `lo > hi`
(`'5' cannot be greater than 2.`, bounds formatted as .NET formats them). `HashCode` hashes by
the runtime's rules and is deterministic across runs, where .NET seeds it per process, so
compare hashes only for equality. `HashCode` lowers to a class, so copying one (`var b = a;`)
shares state (#762). `Add(value, comparer)` and `AddBytes` report MS0001.

## Default text of `bool`, `double` and `float`

Without a format, a `bool`, `double` or `float` converts to text as .NET prints it in the
invariant culture (baseline FR-014, #308): `True` / `False`, `1E-07`, `1E+21`, `-0`,
`NaN`, `Infinity` (JavaScript writes `true`, `1e-7`, `1e+21`, `0`). The conversion calls one
`metano-runtime` function, chosen by the **static type** of the value, in interpolation holes,
string concatenation (`+`, `+=`), `ToString()` and `string.Join` over `bool`, `double` or
`float` values (and over their nullable forms, which print `null` as `""`, as in C#); every other type keeps the
native conversion, so a project that converts none of these three imports none.

| C# | TypeScript |
|----|----|
| `$"{flag}"`, `"x" + flag`, `flag.ToString()` (`bool`) | `` `${formatBoolean(flag)}` ``, `"x" + formatBoolean(flag)`, `formatBoolean(flag)` |
| `$"{d}"`, `d.ToString()` (`double`) | `` `${formatDouble(d)}` ``, `formatDouble(d)` (the `G` format) |
| `$"{f}"`, `f.ToString()` (`float`) | `formatSingle(f)` |
| `$"{flag,8}"` | `alignValue(formatBoolean(flag), 8)` |
| `string.Join(",", flags)` (`IEnumerable<bool>`) | `flags.map((item) => formatBoolean(item)).join(",")` |

`formatSingle` first rounds the number to the `float` C# would hold (the generated code keeps
a `float` in a JavaScript `number` that need not be single precision) and prints the shortest
digits that read back as that single-precision value: `1f / 3` prints `0.33333334`, and the
`float` `0.1f + 0.2f` prints `0.3`. A boxed value (`object o = true`, `string.Join(",", objects)`)
has no static type to read and keeps the JavaScript text. For native JavaScript text use
`[Emit]` or `[MapMethod]`. The digits and notation of every row are checked against real .NET
by `tests/BclReference` (bun and Node); Dart prints these values with its own conversion.

## Number formatting (format strings and interpolation clauses)

A format or alignment clause of an interpolation hole (`$"{n,8:D2}"`) and `ToString(format)`
on a number call one `metano-runtime` formatter, picked by the value's static C# type, so a
project that writes no clause imports none (baseline FR-051). The formatters implement .NET's
standard numeric format strings in the invariant culture, with the same output as .NET 10.

| C# | TypeScript |
|----|----|
| `$"{n:D2}"`, `n.ToString("D2")` (`int n`) | `formatInteger(n, 32, "D2")` |
| `$"{s:X}"` (`short s`) / `$"{b:x2}"` (`sbyte b`) | `formatInteger(s, 16, "X")` / `formatInteger(b, 8, "x2")` |
| `$"{d,10:F2}"`, `d.ToString("F2", provider)` (`double d`) | `formatDouble(d, "F2", 10)`, `formatDouble(d, "F2")` |
| `$"{l:E2}"` (`long l`) | `formatInt64(l, "E2")` |
| `$"{n:D2}"` (`int? n`) | `formatInteger(n, 32, "D2")` (a `null` prints `""`, as in C#) |
| `$"{x,8}"`, `$"{x,-8}"` (any type, no format) | `alignValue(x, 8)`, `alignValue(x, -8)` |

- **Supported formats.** `G[n]`, `R`, `F[n]`, `N[n]`, `E[n]`, `P[n]` and `C[n]` for every
  integer type and `double`, plus `D[n]` and `X[n]` for the integers; upper and lower case
  where .NET distinguishes them (`e`, `g`, `x`); any precision .NET accepts. `C` uses the
  invariant currency symbol `¤` (`¤1,234.50`, a negative value in parentheses), `P` the
  invariant pattern (`12.50 %`).
- **Rounding.** A `double` rounds its exact binary value half to even, as .NET Core 3.0+
  does: `2.675` is `2.67499999…`, so `F2` prints `2.67`, and `0.125` is exact, so `F2`
  prints `0.12` (`toFixed` gives `0.13`, rounding that exact midpoint up). An integer rounds half away from zero
  (`25` with `E0` is `3E+001`). A negative value that rounds to zero keeps its sign
  (`-0.001` with `F2` is `-0.00`). `G` and `R` without a precision print the shortest
  round-trip digits, in .NET's notation (`1E-05`, `1E+21`); `NaN` and the infinities print
  `NaN`, `Infinity`, `-Infinity` in every format.
- **Alignment.** The width counts UTF-16 code units; a positive alignment pads on the left,
  a negative one on the right, and a longer text is not truncated. An alignment without a
  format pads the text the hole prints without the clause: for a `bool`, `double` or
  `float` that is the default text below (`True`, `1E-07`); an enum prints its name or number
  as the target holds it, and a `decimal` drops trailing zeros (decimal.js: `1.10m` prints
  `1.1`; #662).
- **Ignored formats.** C# passes a format only to an `IFormattable` value, so a format on
  any static type that does not implement it — `string`, `bool`, a user class, record or
  struct — is ignored, and so is one on a `char`; the output prints the clause-free hole
  (`$"{plain:X}"` is `${plain}`). `object`, an interface and a type parameter keep the
  format, because the run-time type decides: it reports. The rule reads the static type, so
  it is exact for a sealed class, a struct and a record struct; for a non-sealed class whose
  run-time value implements `IFormattable` (`Open x = new DerivedFmt(); $"{x:X}"`), .NET calls
  the derived `ToString(format, provider)` while the output prints the default text.
- **A format that is the default text.** Where the clause-free text already is .NET's text
  for the format, for every value, the hole and `ToString(format)` print it: a `Guid`'s
  `D`, a numeric enum's `D`, a `[StringEnum]`'s `G` / `F` (when no member is renamed), a
  `DateOnly`'s `O` and the custom `yyyy-MM-dd` (exactly; the same ISO text), and a
  `BigInteger`'s `G` / `R` / `D` (no precision), the standard letters in either case
  (`c.ToString("D")` becomes `` `${c}` ``). The TypeScript target declares this list
  (`TsFormatStrings.PrintsAsDefaultText`).
- **Through the type's mapping.** A hole is `value.ToString(format)`: where a declarative
  mapping lowers that call, the hole lowers through it, so the two cannot disagree. `Guid`
  maps `N` / `n` (no hyphens), `B` / `b` (braces), `P` / `p` (parentheses), and every
  other format to the hyphenated text. A nullable value does not take this path (a `null`
  would reach the mapping); it reports unless the format is the default text.
- **Reported (`MS0001`, warning; the hole or call throws `Unsupported expression:
  UnsupportedFormat` at run time).** A custom numeric format (`0.00`, `#,##0`), the
  integers' `B`, and any format no formatter, default text or mapping covers: on `decimal`,
  `float` and `Half` (their values are not held at their own precision, so the digits would
  differ), `DateTime` / `DateTimeOffset` / `TimeOnly` / `TimeSpan` and the other `DateOnly`
  formats, an enum's other formats, a user `IFormattable` type, `object` or a type
  parameter. The same rule covers `ToString(format)` and `ToString(format, provider)` of any
  BCL type, reported at the format argument (`m.ToString("F2")` on a `decimal`,
  `dt.ToString("O")`, `c.ToString("G")` on an enum, `v.ToString("D2", null)` with
  `T : IFormattable`), unless a declarative mapping lowers it; a user type's own
  `ToString(string)`, in this project or a referenced library, is its own method and is left
  alone, and a static `ToString` (`Convert.ToString(s)`) is not a format call. A BCL type is
  one from an assembly read from metadata, named `System` / `System.*` (or the core library),
  that does not carry `[TranspileAssembly]` / `[EmitPackage]`: a project or transpiled library
  named `System.*` keeps its own methods (a retargeted project reference counts as read from
  metadata, so without the attributes it is treated as BCL). `value?.ToString(format)` takes the same steps as
  the plain call, and an empty or `null` format is the type's default format, as in .NET
  (`mode.ToString("")` prints the member name). The 64-bit integers, `nint` and
  `nuint` included, keep `MS0066` for their unsupported formats (see below). A letter .NET
  rejects (`D` on a `double`, `Q`) is not reported: the formatter throws the same
  `FormatException`. The placeholder throws where the expression is evaluated: an
  unsupported clause in a static field initializer makes the module throw when it is
  imported.
- **A format known only at run time.** `n.ToString(format)` with a variable format on an
  integer or a `double` calls the formatter (`formatInteger(n, 32, format)`,
  `formatDouble(d, format)`), as `formatInt64` does for `long`, and the formatter decides:
  a `null` format is `G` (as `ToString((string?)null)` is on .NET, so a `double` prints
  .NET's `1E-07`, not JavaScript's `1e-7`), a custom format or the integers' `B` throws
  `NotSupportedException` naming the format (`The format '0.00' is not supported for
  double in TypeScript.`), and an invalid one throws `FormatException`
  (`Format specifier was invalid.`). Only a constant unsupported format reports at
  compile time.
- **Dart.** No clause is honored yet: every hole with a format or alignment clause (other
  than an ignored format), and every BCL `ToString(format)`, reports `MS0001` and throws
  `UnsupportedError`.
- **`char` arithmetic.** `{c + 1:D3}` over a `char` operand throws: `char` arithmetic is not
  lowered yet (#501).
- **Unchanged.** A hole without a clause, and `ToString()` without a format, keep the
  native JavaScript conversion.
- **Not covered yet.** `string.Format` composite formatting (`string.Format("{0:F2}", d)`
  keeps its unmapped call and `MS0001`) and `FormattableString` (both #662); custom numeric
  patterns (reported above), even those that equal the default text for every value
  (`{b:#,##0}` on `sbyte` / `byte`, `{d:yyyy'-'MM'-'dd}` on `DateOnly`, `BigInteger`'s
  `D0` / `D1`). One format still prints silently wrong text: `Guid`'s `X`
  (and an invalid `Guid` format, which .NET rejects), which the `Guid.ToString` fallback
  mapping prints as the hyphenated text, in a hole and in a call (tracked in #657: the
  mapping port cannot see the literal filters).

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

## Integers of 32 bits or fewer

`sbyte`, `byte`, `short`, `ushort`, `int` and `uint` are JavaScript `number`s, with no
wrapper type. The compiler makes them behave like C# where C# narrows or reinterprets
(spec 012 FR-074–FR-077, 3.0.0):

| C# | TypeScript (default context) | In a `checked` context |
|----|----|----|
| `(int)u` | `u \| 0` | `checkedInt32(u)` |
| `(uint)i` | `i >>> 0` | `checkedUInt32(i)` |
| `(short)i` | `(i << 16) >> 16` | `checkedNarrowNumber(i, 16, true)` |
| `(ushort)i` | `i & 0xFFFF` | `checkedNarrowNumber(i, 16, false)` |
| `(sbyte)i` | `(i << 24) >> 24` | `checkedNarrowNumber(i, 8, true)` |
| `(byte)i` | `i & 0xFF` | `checkedNarrowNumber(i, 8, false)` |
| `(int)b`, `(byte)(i & 0xFF)` | `b`, `i & 255` (the value cannot change) | same |
| `b += 1;`, `b++;` (`byte b`) | `b = (b + 1) & 0xFF;` | `b = checkedNarrowNumber(b + 1, 8, false);` |
| `u & v`, `u \| v`, `u ^ v`, `~u`, `u << n` (`uint`) | `(u & v) >>> 0`, …, `~u >>> 0`, `(u << n) >>> 0` | same |
| `u >> n` (`uint`) | `u >>> n` | same |
| `i >>> n` (`int`) | `(i >>> n) \| 0` | same |
| `i + j`, `i - j`, `i * j`, `-i`, `i++` | `(i + j) \| 0`, `(i - j) \| 0`, `Math.imul(i, j)`, `-i \| 0`, `i = (i + 1) \| 0` (spec 001 FR-074; `uint`: `>>> 0`) | `checkedInt32(i + j)`, …, `i = checkedInt32(i + 1)` |

- The native operators import nothing; the `checked…` helpers come from
  `metano-runtime` and throw `OverflowException` (`Arithmetic operation resulted in an
  overflow.`). The context is the one C# uses: `checked(...)`, `checked { }` or the
  `CheckForOverflowUnderflow` project option.
- Compound assignments and `++` / `--` on `sbyte`, `byte`, `short` and `ushort` store
  the narrowed value, on locals, fields, properties, indexers and array elements (the
  receiver and the index are evaluated once). `&=`, `|=`, `^=`, `>>=` and `%=` keep the
  native compound operator, because they cannot leave the type's range.
- A conversion from `double` / `float` truncates first (`int32FromDouble(d) & 0xFF`), and
  one from `long` / `ulong` goes through `bigint` (`Number(BigInt.asUintN(8, l))`).
- **Known gap (closed for TypeScript by spec 001 FR-074–FR-076, below; Dart keeps it):**
  `int` / `uint` arithmetic does not wrap in the default (unchecked)
  context. `int.MaxValue + 1` is `2147483648` where C# gives `int.MinValue`; the value
  stays exact up to 2^53. A `checked` context gives C#'s `OverflowException`.
  `int.MinValue / -1` returns `2147483648` instead of throwing. At the edges of the gap:
  - An unchecked overflow that feeds checked arithmetic throws. In a project compiled
    with `CheckForOverflowUnderflow`, `var h = unchecked(a * 31 + 7); return h + 1;` with
    `a = int.MaxValue` is `2147483625` on .NET and an `OverflowException` here, because
    `h` was never wrapped. Narrowing the unchecked result through another type wraps it:
    `unchecked((int)(uint)(a * 31 + 7))` emits `((a * 31 + 7) >>> 0) | 0`, exact while the
    value stays below 2^53.
  - A cast to the operand's own type emits nothing and does not wrap: `(int)(i + j)` and
    `(uint)(u1 - u2)` keep the unwrapped result, while `(uint)(i - j)` and `(int)(u1 + u2)`
    narrow.
  - `uint` bit patterns are normalized and `uint` arithmetic is not: `(a - b) | 0u` is
    C#'s value, `a - b` alone stays negative when it underflows.
  - `checked(a + a % b)` with `b == 0` throws `OverflowException` where .NET throws
    `DivideByZeroException` (the native `%` yields `NaN`).
- A checked conversion of or into a nullable value is checked, for the 64-bit integers too
  (`checked((byte?)ni)`, `checked((int?)l)`, `checked((int)nl)`, `checked((ulong?)ni)`).
- **Dictionary entries:** a compound assignment or an increment whose store needs one of
  these lowerings reports `MS0001` (`'counts[key]++' on a dictionary entry is not
  supported.`): `counts[key]++` on a `Dictionary<string, int>` in a checked context, any
  such store on a `byte` / `sbyte` / `short` / `ushort` value, `flags[key] |= mask` on a
  `uint` value, and in any context `d[k] <<= 1` / `d[k] >>>= 1` on an `int` value and
  `d[k] >>= 1` on a `uint` value. The read-modify-write lowering does not cover
  dictionary entries (the read of a missing key would not throw). A store that
  needs no lowering (`counts[key]++`, `+=` or `>>=` on an `int` entry in the default
  context, `>>>=` on a `uint` entry) is not reported and still emits code that is not
  valid JavaScript, as before. Read, compute and write the entry in separate
  statements.
- Not covered: `char` conversions, `decimal` sources, enum endpoints and `[Queryable]`
  expression trees keep their earlier lowering. Three shapes around them changed anyway:
  `checked(a - b)` on two values of an enum declared narrower than `int` is checked;
  `(byte)(int)c` on a `char` emits `c & 0xFF` on a string, which is `0` and does not
  type-check (TS2362, with or without `--strict`); and
  `checked(c + 1)` on a `char` throws `OverflowException`.
- Dart: an `int` is 64-bit, so the same conversions and stores print
  `.toSigned(n)` / `.toUnsigned(n)` (`(i << n).toSigned(32)`,
  `(i.toUnsigned(32) >>> n).toSigned(32)`). A 32-bit shift count is not masked there, so
  any count outside 0..31, constant or not, differs from C# unless the whole shift is a
  constant expression (`a << 32` is `0`, C# gives `a`), and a negative count throws
  `ArgumentError`. Dart
  never throws in a checked context:
  checked `int` / `uint` arithmetic prints as its unchecked form (`a + b`, no wrap), and
  a checked conversion or narrow store prints the narrowing of its unchecked form.

### `int` / `uint` wrap-around (spec 001 FR-074–FR-076, #639)

In the default (unchecked) context, TypeScript reduces `int` / `uint` arithmetic modulo
2^32 as .NET does, with native operators and no import: `a + b` → `(a + b) | 0`,
`u - v` → `(u - v) >>> 0`, `a * b` → `Math.imul(a, b)` (never `(a * b) | 0`, which loses
the low bits past 2^53), `-a` → `-a | 0`, and `i++`, `i += k`, `u *= k` store the
reduced value (`i = (i + 1) | 0;`, `u = Math.imul(u, k) >>> 0;`). `int.MaxValue + 1` is
`int.MinValue`, `0u - 1u` is `4294967295`.

- **No reduction where C# proves the value fits** (FR-075, a core fact, so the `checked`
  form drops its `checkedInt32` too): operand ranges (`b1 + b2` on `byte`,
  `(x & 0xFF) + 1`, `c + 1` on a `char`), `a.Length - 1` on an array or a `string`,
  `list.Count - 1` on `List<T>`, `Dictionary<,>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`,
  `SortedDictionary<,>`, `SortedList<,>`, `SortedSet<T>`, `LinkedList<T>` (any other
  `Count` spans its type), and the step of a counting `for` whose counter is declared in
  the initializer, written nowhere else, and bounded by the condition
  (`for (let i = 0; i < n; i++)` keeps `i++`).
- **One trailing reduction** (FR-076): an operand whose JavaScript consumer reduces
  modulo 2^32 itself (another wrapped `+` / `-`, `Math.imul`, `&`, `|`, `^`, `~`, a
  shift, an unchecked narrowing) drops its own `| 0`: `a + b + c - a * b` emits
  `(a + b + c - Math.imul(a, b)) | 0`, `(byte)(a + b)` emits `(a + b) & 0xFF`. A
  comparison, `/`, `%`, a conversion to `double` / `long` / `string`, an argument, a
  return value, a store and a checked operation keep the wrap.
- Not reduced: `/`, `%` (`int.MinValue / -1` still returns `2147483648`), results of an
  enum type, user-defined operators, `[Queryable]` expression trees, constants.
- A non-lifted wrapped store on a dictionary entry (`counts[key]++`, `counts[key] += n` on a
  `Dictionary<_, int>`) reports an `MS0001` error, so the build fails as `tsc` did before; the
  checked and narrow-type forms keep their warning.
- Cost: in range the wrap is as fast as the unwrapped `number`; an accumulator that
  overflows on most iterations hits a V8 slow path on Node (about 10 times the asm.js
  `((s | 0) + x) | 0` form; Bun has none). Numbers in spec 001 FR-076.
- Dart is out of scope for 3.0: its output keeps the unwrapped 64-bit `int` result.

## 64-bit integers (`long` / `ulong`)

From spec 012 (3.0.0), `long`, `ulong`, `nint` and `nuint` hold `bigint`, so no digit
is lost past 2^53 (Snowflake ids, database keys, tick counts, hashes). It is the only
mapping: there is no option to keep the 2.x `number` output. The full contract is
[`specs/012-long-as-bigint/`](../specs/012-long-as-bigint/spec.md); the JSON side is in
[docs/serialization.md](serialization.md#64-bit-numbers).

### Arithmetic, overflow, and `checked`

Every C# arithmetic result is reduced back into the 64-bit domain the way C# reduces
it: an unchecked context wraps with the native `BigInt.asIntN(64, …)` /
`BigInt.asUintN(64, …)`, a checked context calls the runtime
`checkedInt64(…)` / `checkedUInt64(…)`, which throw `OverflowException` with .NET's
message. The context comes from Roslyn (`checked { }`, `unchecked { }`, or the
`CheckForOverflowUnderflow` project option). Division and remainder go through
`divInt64` / `remInt64` / `divUInt64` / `remUInt64` (`DivideByZeroException` on a zero
divisor, `OverflowException` for `long.MinValue / -1` in both contexts); a constant
divisor that cannot throw stays inline (`a / 7n`). Shift counts are masked
(`x << 70` → `x << 6n`; a variable count becomes `BigInt(n & 63)`), `>>>` on `long`
re-wraps through `BigInt.asUintN`, and `/` by zero throws for `long` too.

### Conversions

Every conversion across a 64-bit integer is explicit, from Roslyn's semantic model:

| C# | TypeScript |
|----|----|
| `long x = i;` (`int i`) | `BigInt(i)` |
| `(int)l` | `Number(BigInt.asIntN(32, l))` (checked: `Number(checkedNarrow(l, 32, true))`) |
| `(long)d` (`double d`) | `int64FromDouble(d)` (truncate, saturate, NaN → `0n`; checked variant throws) |
| `(double)l` | `Number(l)` |
| `(float)l` | `int64ToSingle(l)` (one rounding, like C#) |
| `(long)m` (`decimal m`) | `int64FromDecimal(m)` (always checked, like C#) |
| `(long)b` (`BigInteger b`) | `int64FromBigInteger(b)` |
| `(int)d` | `int32FromDouble(d)` (3.0.0: floating-point casts truncate now instead of being erased — `(int)3.7` is `3`) |
| `l == 5`, `l < i` | `l === 5n`, `l < i` (a `bigint` and an integer `number` compare exactly) |

`uint op int` promotes to `long` in C#, so `ui + i`, `ui * i` and `-ui` produce
`bigint` expressions even in code that never names `long` (relational `ui < i` stays
`number`).

### `Math` and the 64-bit statics

| C# | TypeScript |
|----|----|
| `Math.Abs(l)` / `long.Abs(l)` | `absInt64(l)` (`long.MinValue` throws) |
| `Math.Max(a, b)` / `Math.Min` | `maxInt64(a, b)` / `minInt64(a, b)` (and `…UInt64`) |
| `Math.Sign(l)` | `signInt64(l)` |
| `Math.Clamp(v, lo, hi)` | `clampInt64(v, lo, hi)` |
| `Math.DivRem(a, b)` | `divRemInt64(a, b)` (a mutable `[quotient, remainder]`) |
| `Math.BigMul(a, b, out var low)` | `const [high, low] = bigMulInt64(a, b)` |
| `arr[l]`, `arr.LongLength`, `new int[l]` | `arr[Number(l)]`, `BigInt(arr.length)`, `new Array<number>(Number(l)).fill(0)` |

The `nint` / `nuint` overloads (`IntPtr` / `UIntPtr`) route to the same helpers. A
`List<T>` or `string` indexer takes an `int`, so `xs[(int)l]` lowers the `(int)`
conversion (`xs[Number(BigInt.asIntN(32, l))]`); only an array takes the 64-bit index
directly.

### Formatting, parsing, `Convert`, LINQ

| C# | TypeScript |
|----|----|
| `l.ToString("N0")`, `$"{l,8}"`, `$"{l:X}"` (also `nint` / `nuint`) | `formatInt64(l, "N0")`, `formatInt64(l, null, 8)`, `formatInt64(l, "X")` (`G D N F E P C X R` in the invariant culture, see [Number formatting](#number-formatting-format-strings-and-interpolation-clauses); `B` or a custom format reports `MS0066` and throws) |
| `long.Parse(s)` / `ulong.Parse(s, NumberStyles.HexNumber)` | `parseInt64(s)` / `parseUInt64(s, 515)` |
| `long.TryParse(s, out var x)` | `const [ok, x] = tryParseInt64(s)` |
| `Convert.ToInt64(d)` / `(s)` / `(s, 16)` | `convertDoubleToInt64(d)` / `convertStringToInt64(s)` / `convertStringToInt64(s, 16)` |
| `Convert.ToInt32(l)` | `Number(convertNarrow(l, "Int32"))` |
| `l.CompareTo(m)` / `l.Equals(m)` / `l.GetHashCode()` | `compareInt64(l, m)`, `l === m`, `valueHashCode(l)` (the .NET word fold) |
| `ids.Sum()` / `ids.Average()` / `ids.Max()` | `linq(ids, sumInt64())` / `averageInt64()` / `maxInt64Of()` (the checked 64-bit terminals) |
| `ts.Ticks` / `TimeSpan.FromTicks(l)` / `dt.Ticks` | `durationTicks(ts)` / `durationFromTicks(l)` / `plainDateTimeTicks(dt)` |
| `Console.WriteLine(l)` | `console.log(String(l))` |

### Runtime type tests and dispatch

`isInt64` / `isUInt64` test `typeof v === "bigint"` plus the range (DECIDED-14), so
overload dispatch distinguishes `M(int)` from `M(long)` by the runtime type. Same-arity
overloads that differ only among `long`, `ulong` and `BigInteger` are tried in that
order. One divergence from C#, recorded by the spec's Edge Cases table and true for any
value that reaches an `object` slot: the raw `bigint` carries no static type, so the
test reads the range — `object o = 5UL; o is long` is `true` in TypeScript where C#
says `false` (the analogue of 011's number-box divergence). A `bigint` outside the
`long` range also answers `o is long` with `false` and `o is ulong` with `true`, but the
test is a range check, not a stored box.

In 2.x `isInt64` / `isUInt64` were `number` range checks over the safe integers, and
`tryParseInt64` returned a `number`; 3.0 replaces all three with the `bigint` versions
under the same names (see the [CHANGELOG](../CHANGELOG.md)).

### Performance

`bigint` arithmetic costs more than `number` in tight loops; the cost is documented,
not hidden (NFR-006). The benchmark harness is
`targets/js/metano-runtime/bench/int64/` (`bun run bench:int64`), median of 10 runs
after 3 warm-ups, one fresh process per runtime. Recorded 2026-11-06 on
Apple M1 Pro, 16 GB, macOS arm64 (Darwin 25.6.0), Node v24.11.0, Bun 1.4.2 —
a review artifact, not a CI gate:

| Case | Bun bigint | Bun number | ratio | Node bigint | Node number | ratio |
|----|----|----|----|----|----|----|
| sum 1e6 (wrap vs plain) | 15.73 ms | 1.20 ms | 13.1× | 8.40 ms | 6.03 ms | 1.4× |
| FNV-1a hash mix 1e5 strings | 87.44 ms | 2.71 ms | 32.3× | 12.86 ms | 8.92 ms | 1.4× |
| division 1e6 (helper + constant vs number) | 1.19 ms | 0.04 ms | ~1× per op | 0.52 ms | 0.01 ms | ~1× per op |
| `formatInt64("N0")` 1e5 vs `toLocaleString` | 15.44 ms | 1537.14 ms | 0.01× | 22.60 ms | 1633.36 ms | 0.01× |
| `parseInt64` 1e5 vs `Number` | 23.20 ms | 1.81 ms | 12.8× | 26.95 ms | 5.27 ms | 5.1× |
| JSON round-trip 1e5 records | 72.56 ms | 26.51 ms | 2.7× | 103.84 ms | 48.80 ms | 2.1× |

Readings: the wrap is cheap where V8 (Node) lowers `BigInt.asIntN` to native 64-bit
arithmetic (sum 1.4×), while JavaScriptCore (Bun) pays ~13×; hash mixing pays the
multiply path (32× on JSC, 1.4× on V8); `divInt64` and inline `bigint` division run at
about the `number` cost on V8; `formatInt64` beats `Intl` by two orders of magnitude on
both runtimes; the exact JSON path costs 2–3× the `number` round trip. The division row
compares one helper call plus one inline division per element against one `number`
division per element, so the ratio reads per-operation, not per-case.

## Exceptions

Each BCL exception below is a `metano-runtime` class (spec 011 FR-013, FR-014), one module
per class under `metano-runtime/system/exceptions/`, bound to its C# type by an
`[ExportFromBcl]` entry in `Metano/Runtime/Exceptions.cs`. Every class extends `Exception`,
which extends `Error`, so `instanceof Error` holds for all of them.

| C# | TypeScript class | Constructor (roles, in order) |
|----|------------------|-------------------------------|
| `Exception` | `Exception` | `(message, innerException)` |
| `SystemException` | `SystemException : Exception` | `(message, innerException)` |
| `InvalidOperationException` | `InvalidOperationException : SystemException` | `(message, innerException)` |
| `ArgumentException` | `ArgumentException : SystemException` | `(message, paramName, innerException)` |
| `ArgumentNullException` | `ArgumentNullException : ArgumentException` | `(paramName, message, innerException)` |
| `ArgumentOutOfRangeException` | `ArgumentOutOfRangeException : ArgumentException` | `(paramName, actualValue, message, innerException)` |
| `KeyNotFoundException` | `KeyNotFoundException : SystemException` | `(message, innerException)` |
| `IndexOutOfRangeException` | `IndexOutOfRangeException : SystemException` | `(message, innerException)` |
| `FormatException` | `FormatException : SystemException` | `(message, innerException)` |
| `ArithmeticException` | `ArithmeticException : SystemException` | `(message, innerException)` |
| `OverflowException` | `OverflowException : ArithmeticException` | `(message, innerException)` |
| `DivideByZeroException` | `DivideByZeroException : ArithmeticException` | `(message, innerException)` |
| `NotSupportedException` | `NotSupportedException : SystemException` | `(message, innerException)` |
| `NotImplementedException` | `NotImplementedException : SystemException` | `(message, innerException)` |
| `InvalidCastException` | `InvalidCastException : SystemException` | `(message, innerException)` |
| `NullReferenceException` | `NullReferenceException : SystemException` | `(message, innerException)` |
| `MissingMethodException` | `MissingMethodException : SystemException` | `(message, innerException)` |
| `ObjectDisposedException` | `ObjectDisposedException : InvalidOperationException` | `(objectName, message, innerException)` |
| `OperationCanceledException` | `OperationCanceledException : SystemException` | `(message, innerException)` |
| `TaskCanceledException` | `TaskCanceledException : OperationCanceledException` | `(message, innerException)` |
| `System.Text.Json.JsonException` | `JsonException : Exception` | `(message, innerException)` |
| `TimeoutException` | `TimeoutException : SystemException` | `(message, innerException)` |
| `System.Runtime.CompilerServices.SwitchExpressionException` | `SwitchExpressionException : InvalidOperationException` | `(unmatchedValue, message, innerException)` |
| `TypeInitializationException` | `TypeInitializationException : SystemException` | `(typeName, innerException)`; thrown by the lazy static-constructor guard (FR-066). A C# construction passes `fullTypeName` as `typeName`. |

- **Construction.** `new X(...)` emits `new X(...)` with a value import from the class's
  module. Each argument is classified by the role of the C# constructor parameter it binds
  to (positional or named; `inner` counts as `innerException`) and passed in the runtime
  constructor's order, with `undefined` for a role omitted ahead of one that is passed:
  `new ArgumentOutOfRangeException("p", "m")` is
  `new ArgumentOutOfRangeException("p", undefined, "m")`. A `null` literal counts as absent.
  Other parameters (`JsonException`'s `path`, a `CancellationToken`, `InvalidCastException`'s
  `errorCode`) are dropped; side-effecting arguments are still evaluated once, in source order.
- **Message.** The runtime class composes `message` as .NET does: the default message when
  none is given (or it is `null`), ` (Parameter 'name')` after a non-empty parameter name,
  `\nActual value was X.` after a non-`null` actual value (formatted with `String`, so `true`
  where .NET writes `True`), `\nObject name: 'x'.` after a non-empty object name. A user
  exception deriving from `System.Exception` without a message passes .NET's
  `Exception of type 'Ns.Type' was thrown.` (the runtime cannot know the derived class's C#
  name); a class deriving from that one without a constructor of its own keeps its base's
  text (.NET names the most-derived class).
- **Members.** `Message` is `message`; `InnerException` reads `cause` and `StackTrace` reads
  `stack` (so both also work on a plain JavaScript `Error`); `ParamName`, `ActualValue` and
  `ObjectName` are `paramName`, `actualValue` and `objectName`. `ToString()` prints .NET's
  `System.InvalidOperationException: message` format, with the inner exception after it. A
  user exception declares its C# type name as `name` and overrides the runtime's `$typeName` /
  `$typeNamespace` hooks (a `$` no C# member can carry) with its type name and namespace, so
  `ToString()` prints `Shop.UnsupportedCurrencyException: …` as .NET does (a nested class
  joins its containing classes with `.` where .NET writes `+`; a generic one keeps its open
  name, ``Box`1``, without the type arguments).
- **Type position, `extends`, type tests.** A BCL exception in a signature names its class
  (`error: Exception`), a user exception deriving from one extends it (`class
  InvalidSlugException extends ArgumentException`), and `is` / `as` / type patterns / switch
  arms / overload-dispatcher arms test `instanceof` it. `catch (X)` clauses test it too (the
  single-`catch` chain of spec 011 FR-015, see [`docs/architecture.md`](architecture.md));
  `catch (Exception)` and a general `catch` take every thrown value, including one that is not
  an `Error`. A chain with a typed arm first normalizes the caught value with `toException`
  (`const e0 = toException(e$);`): a `TypeError` is a `NullReferenceException`, an `AbortError` a
  `TaskCanceledException`, any other `Error` an `Exception` (keeping `message`, `stack` and the
  original as `innerException`), a thrown non-`Error` value an `Exception`; a `when` filter that
  throws is `false` (`tryFilter`). Outside a `catch`, a test on `System.Exception` itself is
  `instanceof Error`, so a JavaScript error (a `TypeError`) is an `Exception`. A `[GenerateGuard]`
  field typed as a BCL exception tests `instanceof` its class (`Error` for `System.Exception`
  and for an unmapped type, which names the root: a structural widening). An overload
  dispatcher tries its arms in declaration order, so with `F(Exception)` declared before
  `F(ArgumentException)` a call from hand-written JavaScript takes the first arm for an
  `ArgumentException` too (C# calls bind statically; user classes behave the same in 2.x).
- **Argument guards.** `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`
  / `ThrowIfNullOrWhiteSpace` and the `ArgumentOutOfRangeException.ThrowIf*` comparisons
  (`ThrowIfZero`, `ThrowIfNegative`, `ThrowIfNegativeOrZero`, `ThrowIfEqual`,
  `ThrowIfNotEqual`, `ThrowIfGreaterThan`, `ThrowIfGreaterThanOrEqual`, `ThrowIfLessThan`,
  `ThrowIfLessThanOrEqual`) lower to runtime functions with .NET's messages; the parameter
  name is the `[CallerArgumentExpression]` text C# supplies, or the one the call passes.
  `throwIfNull` and the string guards assert their argument non-null. The sign guards map for
  the numbers generated code holds (and `char`), testing the sign bit as .NET does (`-0` and
  `NaN` are negative); the ordered guards map for those, `bool` and the date and time types,
  with `double.CompareTo`'s `NaN` order; the equality guards map for those, strings, `Guid`,
  enums, transpiled structs, record structs and sealed records (generic ones included, each type argument read as the type it names) whose fields are such values all the way down, read from source (`[PlainObject]` ones included) and tuples of them,
  with `EqualityComparer<T>.Default`'s rules through the runtime's `valueEquals` (`NaN` equals
  `NaN`, `0` equals `-0`, records, tuples and decimals compare by value, and two `TimeSpan`s are
  equal when they last as long, `PT5M` and `PT300S`; a duration read from ISO text with
  weeks, months or years, `P1W`, compares field by field, and `valueHashCode` hashes the same
  quantity). A
  comparison guard over another `T` (an ordered `string`, culture-sensitive on .NET, or `Guid`;
  `Complex`; a class or an array, which .NET compares by reference; `DateTimeOffset` for
  equality, whose Temporal value also compares the time zone; a type parameter) reports
  `MS0001`. `Guid.Parse` keeps the text's letter case, so two `Guid`s parsed from
  differently cased text compare unequal here where .NET finds them equal. The message texts
  are .NET's, but values quoted inside a message are formatted by JavaScript, not .NET (`true` for `True`, an ISO date for `1/2/2024`, `PT5M` for a `TimeSpan`'s `00:05:00`, `1e+21`, `[object Object]` for a record). A `null` parameter
  name leaves the name empty, as on .NET.
  `ObjectDisposedException.ThrowIf` and every other static member of an exception type
  without a mapping report `MS0001`.
- **Other exceptions.** A BCL exception without an entry (`IOException`,
  `UnauthorizedAccessException`, …) has no runtime class: constructing it reports `MS0001`
  and emits `namedException("System.IO", "IOException", message, inner)`, a runtime
  `Exception` whose message is .NET's and whose `name` / `ToString()` report the C# type; a
  user exception deriving from it extends `Exception` (with the C# type's default message)
  and reports `MS0001` at its base list; an `is` / `as` / pattern / `catch` clause on it is the
  `MS0045` error, since testing `Exception` would take every exception; an overload-dispatcher
  arm keeps the object check. A project can bind one to its own JavaScript class with
  `[assembly: ExportFromBcl(typeof(IOException), ExportedName = "IoError", FromPackage =
  "my-errors")]`: a class outside `metano-runtime/system/exceptions/` takes
  `(message, innerException)`, and a role it does not take (`paramName`, `actualValue`,
  `objectName`) is written into the message as .NET's text (` (Parameter 'p')`, …). A
  `Version` on such an entry is ignored when `FromPackage` is a module specifier.
- **Runtime helpers** throw these classes with .NET's messages (spec 011 FR-016): integer
  division by zero, number parsing and `decimal.Parse`, checked conversions, `string.Replace`,
  `Dictionary.Add`, the LINQ terminals, `Math.Round` / `decimal.Round`, the `DateOnly` /
  `TimeOnly` / `DateTime` component constructors, a negative array length, a non-exhaustive
  switch expression (`SwitchExpressionException`) and the JSON serializer.
- **Known deviations.** A `TypeError` thrown by JavaScript (a member access on `null`) is not
  a `NullReferenceException` (`toException` of a catch chain converts it, spec 011 FR-015; hand-written
  code that tests a caught value outside a chain sees the `TypeError`). Indexers out of range, a missing
  dictionary key and several `string` / LINQ argument checks throw nothing or a JavaScript
  error (spec 011 FR-016 lists them). Dart keeps its native exception types.

## `System.Text.Json` serializer calls (#741)

| C# | TypeScript |
|---|---|
| `JsonSerializer.Serialize(value, JsonTypeInfo<T>)`, `Serialize(object?, JsonTypeInfo)` | `JsonSerializer.serializeText(value, spec)` — STJ's default text (escapes, number text, exact `decimal` / `long`) |
| `JsonSerializer.Deserialize(string, JsonTypeInfo<T>)`, `Deserialize(string, JsonTypeInfo)` | `JsonSerializer.deserializeText(text, spec)` |
| `MyContext.Default` (a `JsonSerializerContext`) | `MyContext.default` |

TS-only. The other overloads (options, `Type`, streams, writers, spans, `JsonElement`) report
`MS0001`. Details and the measured semantics are in
[serialization.md](serialization.md#serializing-from-transpiled-c-741).

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
compiler. See [Attributes Reference](attributes.md#declarative-bcl-mappings). A template
imports what it prints: a template without `$this` that names the receiver's type as an
identifier (`new Decimal(parseDecimalText($0))`) gets a value import of that type's canonical
exported name; a runtime helper it names must be listed in `RuntimeImports`.

### 2. `[Emit]` — inline JS at call sites

For one-off cases, decorate a method with `[Emit("$0.foo($1)")]` and the call is
replaced verbatim at the call site.

### 3. `[Import]` — wrap an npm module

Declare a C# facade with `[Import(from: "some-pkg")]` and use it as a normal
type. The transpiler emits import statements and package.json entries but never
transpiles the declaration itself.

### 4. `Js.Code` — inline TypeScript at one use site

`Metano.Interop.Js.Code("…")` / `Js.Code<T>("…")` emits a compile-time-constant
string verbatim, for one-off glue that needs no declaration (spec 011 FR-031 –
FR-034):

```csharp
var now = Js.Code<double>("performance.now()");   // (performance.now()) as number
Js.Code($"{element}.scrollIntoView({{ block: {mode} }})");   // statement text
var div = Js.Code($"{Inferno.CreateElement}('div')");   // the hole pulls the facade's import
```

The argument is a constant string, or an interpolated string whose holes are C#
expressions: each hole is transpiled (naming, mappings, imports) and spliced as an
operand, and `{{` / `}}` escape braces. The text itself is opaque: the import
collector, the runtime-requirement scanner, the golden bundle check and the
diagnostics never look inside it, so a name the text spells pulls no import — splice
a facade in a hole instead. A hole inside a JS string literal of the text (`'{name}'`)
warns with `MS0056`, because the value is spliced as code, not quoted.

How the four relate: `[MapMethod]` / `[MapProperty]` map an existing BCL member at
every call site; `[Emit]` and `[Import]` declare a reusable, typed facade for a JS API;
`Js.Code` is a use-site escape hatch with no declaration, no type and no reuse. Use a
facade for anything called twice or exposed in a signature, and `Js.Code` for the
rest. `Js.Code` is TypeScript-only (`MS0051` in any other target, unless the member is
`[Ignore(TargetLanguage.Dart)]`), and on .NET it throws
`PlatformNotSupportedException`. The incremental cache stays correct, since the text
changes with the source.

## See also

- [Attribute Reference](attributes.md) — `[MapMethod]`, `[Emit]`, `[Import]` details
- [Architecture Overview](architecture.md) — how the mapping registry works internally

## Characters, text builders and other members

Members that had no mapping and printed a reference nothing declares (#686, #584, #617, #403). Each lowers to
an expression or a `metano-runtime` helper; an overload that is not listed stays unmapped and reports `MS0001`.

| C# | TypeScript |
|----|----|
| `char.IsLetter(c)`, `IsDigit`, `IsLetterOrDigit`, `IsNumber`, `IsUpper`, `IsLower`, `IsPunctuation`, `IsSymbol`, `IsSeparator`, `IsControl`, `IsWhiteSpace` | `charIsLetter(c)` ... `charIsWhiteSpace(c)`: Unicode general categories as .NET reads them (the engine's Unicode version). The `(string, int)` overloads stay unmapped. |
| `char.IsAscii(c)`, `IsAsciiDigit`, `IsAsciiLetter`, `IsAsciiLetterLower`, `IsAsciiLetterUpper`, `IsAsciiLetterOrDigit`, `IsAsciiHexDigit`, `IsAsciiHexDigitLower`, `IsAsciiHexDigitUpper`, `IsBetween(c, min, max)` | `charIsAscii(c)` ... `charIsBetween(c, min, max)`: code-unit comparisons, as .NET does. |
| `BigInteger.Zero`, `One`, `MinusOne` | `0n`, `1n`, `BigInt(-1)` |
| `b.IsZero`, `IsOne`, `IsEven` | `(b === 0n)`, `(b === 1n)`, `(b % 2n === 0n)` |
| `Convert.ToDouble(x)` for `int`-sized integers, `double`, `float` | `x` |
| `Convert.ToDouble(decimal)`, `decimal.ToDouble(d)` | `d.toNumber()` |
| `Convert.ToDouble(bool)`, `Convert.ToDouble(string)` | `(b ? 1 : 0)`, `convertStringToDouble(s)` (`null` is `0`, bad text throws `FormatException`) |
| `EqualityComparer<T>.Default` | `defaultEqualityComparerFor()`: value equality for records, structs and boxes |
| `a.CompareTo(b)` for `int`, `uint`, `double`, `float` | `compareNumber(a, b)`: `-1`, `0`, `1`; `NaN` sorts first |
| `a.CompareTo(b)` for `short`, `ushort`, `byte`, `sbyte`, `char` | `(a - b)` (the difference, as .NET returns it); a `char` compares by code unit |
| `new StringBuilder(...)`, `Append` (text, `char`, integers, `bool`, `double`, `float`, `object`, `char` repeated), `AppendLine`, `Insert(int, string)`, `Remove`, `Replace`, `Clear`, `Length`, `ToString()`, `ToString(start, length)` | `StringBuilder` of `metano-runtime`; `AppendLine` adds `\n`. The `char[]`, span, `StringBuilder`, `AppendFormat` overloads, the indexer and `Capacity` stay unmapped. |
| `new Lazy<T>(factory)`, `.Value`, `.IsValueCreated` | `Lazy<T>` of `metano-runtime`: the factory runs once, its exception is cached, a factory reading its own value throws `InvalidOperationException`. The `bool` argument is dropped. |
| `new TaskCompletionSource<T>()`, `SetResult`, `SetException`, `SetCanceled`, `TrySet*`, `.Task` | `TaskCompletionSource<T>` of `metano-runtime` (a promise settled from outside) |

### `char`, `int` and conversion fixes (#695)

| C# | TypeScript |
|----|----|
| `int.MaxValue`, `char.MaxValue` and the other limits of `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `char` | the folded literal (`2147483647`, `"￿"` read as `65535`); before, `Int32.maxValue` threw a `ReferenceError` |
| `new string(chars)` / `new string(c, n)` | `chars.join("")` / `c.repeat(n)`; the range, span and pointer overloads stay unmapped (`MS0001`) |
| `int[] a = { c, 'b' };` | the array literal `[c.charCodeAt(0), 98]` |
| `foreach (var c in s)` and LINQ over a `string` | the UTF-16 code units, so a surrogate pair is two elements, as in .NET: `foreach` indexes the string (`for (let $i = 0; $i < $s.length; $i++) { const c = $s[$i]!; … }`, the string bound once), and `linq` reads a string source through a lazy code-unit iterator, so `First`/`Any`/`Take` stop early and no array is copied |
| `c++`, `c += k` on a `char?` | lifted: `null` stays `null`, a value steps its code |
| `(char)m` (`decimal`) | `String.fromCharCode(charCodeFromDecimal(m))`: truncates, throws `OverflowException` outside 0–65535 |
| `(decimal)d` / `(decimal)f` | `new Decimal(decimalTextFromDouble(d, 15))` / `(f, 7)`: rounded to the significant digits .NET 10 keeps, half to even, at most 28 decimal places (`(decimal)(0.1 + 0.2)` is `0.3`, `(decimal)1000000000000005.0` is `1000000000000000`, `(decimal)1e-30` is `0`); NaN, the infinities and values beyond `decimal.MaxValue` throw `OverflowException` |
| `(float)d`, `(float)i`, `float f = i` (`double`, `int`, `uint`) | `Math.fround(d)` |
| `float` `a + b`, `-`, `*`, `/`, the same compound assignments, `++` / `--` (lifted forms included), and LINQ `Sum` / `Average` over `float` | rounded to single precision: `Math.fround(a * b)`, `f = Math.fround(f + 1)`, `Math.fround(linq(xs, sum$rt()))`, so a `float` computed by arithmetic equals the same value cast to `float`; `%`, `%=` and unary `-` are exact and stay native. A `float?` `Sum` / `Average` is not rounded yet |
| `float.Parse(s)`, `float.TryParse(s, out f)`, a JSON number or string read into a `float` | `parseFloat32(s)` / `tryParseFloat32(s)` and the JSON `Single` reader return the value rounded to single precision, so `float.Parse("0.1") * 1f == float.Parse("0.1")` holds |
| `a % b` on `int` / `uint` with a divisor that is not a constant | `intRem(a, b)`: throws `DivideByZeroException` for `0` and `OverflowException` for `int.MinValue % -1`; a constant divisor keeps `%` |
| `a / b` on `int` | `intDiv(a, b)` now also throws `OverflowException` for `int.MinValue / -1` |
| `Math.Abs(int)` | `absInt32(x)`: throws `OverflowException` for `int.MinValue` |
| `(T)x` through a user conversion whose parameter needs an explicit numeric step (`(UZ)longValue` over `explicit operator UZ(uint)`, `(UZ)3.7`) | the step runs first, checked in a `checked` context, truncating a fraction |

Still open: `M(char)` and `M(string)` overloads cannot be told apart by a runtime dispatcher
(both are JS strings), generic math through a type parameter does not wrap, a `ref` argument
is passed by value, and a compound `%=` keeps the native operator.
